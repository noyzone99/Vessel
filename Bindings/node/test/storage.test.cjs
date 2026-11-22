'use strict';

const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const { once } = require('node:events');
const { mkdtemp, rm } = require('node:fs/promises');
const { createRequire } = require('node:module');
const { tmpdir } = require('node:os');
const path = require('node:path');
const { Readable } = require('node:stream');
const { pipeline, finished } = require('node:stream/promises');
const { setImmediate } = require('node:timers/promises');
const { test } = require('node:test');
const { Worker } = require('node:worker_threads');

const consumer = process.env.VESSEL_NODE_CONSUMER;
const binding = consumer ? createRequire(path.join(consumer, 'package.json')).resolve('vessel-storage')
  : require.resolve('../index.cjs');
const { Vessel, VesselError } = require(binding);
const timeout = 15_000;

async function store(t) {
  const directory = await mkdtemp(path.join(tmpdir(), 'vessel-node-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const vessel = await Vessel.open(directory);
  return { vessel, directory };
}

async function read(stream) {
  const chunks = [];
  for await (const chunk of stream) chunks.push(chunk);
  return Buffer.concat(chunks);
}

test('put, stream, metadata, keys, delete and reopen', { timeout }, async t => {
  await assert.rejects(Vessel.open(''), TypeError);
  const { vessel, directory } = await store(t);
  try {
    const data = Buffer.from('Hello, Vessel!');
    const meta = await vessel.put('docs/é', data, { compression: 'lz4', mimeType: 'text/plain' });
    assert.equal(meta.originalSize, BigInt(data.length));
    assert.equal(meta.contentHash.length, 64);
    assert(meta.createdAt instanceof Date);
    assert.deepEqual(await vessel.getMetadata('docs/é'), meta);
    assert.deepEqual(await vessel.keys('docs/'), ['docs/é']);
    await assert.rejects(vessel.put('docs/é', data), { code: 'ERR_VESSEL_ALREADY_EXISTS' });
    assert.deepEqual(await read(vessel.createReadStream('docs/é', { highWaterMark: 7 })), data);
    assert.equal((await vessel.put('empty', Buffer.alloc(0))).originalSize, 0n);
    assert.equal((await read(vessel.createReadStream('empty'))).length, 0);
    assert.equal(await vessel.delete('empty'), true);
    assert.equal(await vessel.delete('empty'), false);
    await assert.rejects(read(vessel.createReadStream('empty')), {
      name: VesselError.name, code: 'ERR_VESSEL_NOT_FOUND',
    });
    await vessel.close();
    await vessel.close();
    await assert.rejects(vessel.keys(), { code: 'ERR_VESSEL_DISPOSED' });
    const reopened = await Vessel.open(directory);
    try { assert.deepEqual(await read(reopened.createReadStream('docs/é')), data); }
    finally { await reopened.close(); }
  } finally { await vessel.close(); }
});

test('rejects malformed UTF-16 without replacing valid text', { timeout }, async t => {
  const { vessel, directory } = await store(t);
  try {
    const key = 'docs/\uFFFD';
    const original = Buffer.from('original');
    await vessel.put(key, original);
    await assert.rejects(vessel.put('docs/\uD800', Buffer.from('replacement'), { overwrite: true }), TypeError);
    assert.deepEqual(await read(vessel.createReadStream(key)), original);

    const malformed = 'docs/\uDC00';
    await assert.rejects(vessel.keys(malformed), TypeError);
    await assert.rejects(vessel.getMetadata(malformed), TypeError);
    await assert.rejects(vessel.delete(malformed), TypeError);
    assert.throws(() => vessel.createReadStream(malformed), TypeError);
    assert.throws(() => vessel.createWriteStream(malformed), TypeError);
    await assert.rejects(vessel.put('metadata', original, { fileName: malformed }), TypeError);
    await assert.rejects(Vessel.open(path.join(directory, malformed)).then(opened => opened.close()), TypeError);

    const unicode = 'docs/\u{1F4C4}';
    assert.equal((await vessel.put(unicode, original, { fileName: unicode })).fileName, unicode);
    assert.deepEqual(await vessel.keys(unicode), [unicode]);
  } finally { await vessel.close(); }
});

test('concurrent uploads and a reader retain the expected object version', { timeout }, async t => {
  const { vessel } = await store(t);
  try {
    const data = Buffer.alloc(1024 * 1024 + 19, 42);
    async function* source() {
      yield data.subarray(0, 3);
      await setImmediate();
      yield data.subarray(3);
    }
    await Promise.all(['none', 'lz4', 'zstd'].map(compression =>
      pipeline(Readable.from(source()), vessel.createWriteStream(compression, { compression }))));
    const snapshot = vessel.createReadStream('zstd', { highWaterMark: 8192 });
    await once(snapshot, 'readable');
    await vessel.put('zstd', Buffer.from('replacement'), { overwrite: true });
    await vessel.delete('zstd');
    await vessel.close();
    assert.deepEqual(await read(snapshot), data);
  } finally { await vessel.close(); }
});

test('abort and storage errors settle an upload even while its source is idle', { timeout }, async t => {
  const { vessel } = await store(t);
  try {
    await vessel.put('present', Buffer.from('old'));
    const idle = new Readable({ read() {} });
    await assert.rejects(pipeline(idle, vessel.createWriteStream('present')), {
      code: 'ERR_VESSEL_ALREADY_EXISTS',
    });
    assert(idle.destroyed);

    const controller = new AbortController();
    const target = vessel.createWriteStream('present', { overwrite: true, signal: controller.signal });
    const writing = finished(target);
    await new Promise((resolve, reject) => target.write(Buffer.alloc(1024 * 1024), error =>
      error ? reject(error) : resolve()));
    target.write(Buffer.alloc(4 * 1024 * 1024));
    controller.abort();
    await assert.rejects(writing, { name: 'AbortError' });
    assert.deepEqual(await read(vessel.createReadStream('present')), Buffer.from('old'));

    const abandoned = vessel.createWriteStream('abandoned');
    const abandonedDone = assert.rejects(finished(abandoned), { code: 'ERR_VESSEL_CANCELLED' });
    await new Promise((resolve, reject) => abandoned.write('pending', error =>
      error ? reject(error) : resolve()));
    await vessel.close();
    await abandonedDone;
  } finally { await vessel.close(); }
});

test('terminating a worker cancels its open upload and releases the store', { timeout }, async t => {
  const directory = await mkdtemp(path.join(tmpdir(), 'vessel-node-worker-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const worker = new Worker(`
    const { parentPort, workerData } = require('node:worker_threads');
    const { Vessel } = require(workerData.binding);
    (async () => {
      const store = await Vessel.open(workerData.directory);
      const stream = store.createWriteStream('incomplete');
      stream.write(Buffer.alloc(512 * 1024), () => parentPort.postMessage('ready'));
    })().catch(error => { throw error; });
  `, { eval: true, workerData: { binding, directory } });
  try { await once(worker, 'message'); }
  finally { await worker.terminate(); }
  const reopened = await Vessel.open(directory);
  try { assert.deepEqual(await reopened.keys(), []); }
  finally { await reopened.close(); }
});

test('the installed package also exports Vessel to ESM', { skip: !consumer, timeout }, () => {
  execFileSync(process.execPath, ['--input-type=module', '-e', `
    import { Vessel, VesselError } from 'vessel-storage';
    if (Vessel.name !== 'Vessel' || VesselError.name !== 'VesselError') process.exitCode = 1;
  `], { cwd: consumer });
});
