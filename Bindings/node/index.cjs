'use strict';

const { Buffer } = require('node:buffer');
const path = require('node:path');
const { Readable, Writable } = require('node:stream');
const { types } = require('node:util');

const chunkSize = 64 * 1024;
const construction = Symbol();
const compression = ['none', 'zstd', 'lz4'];
const levels = ['fastest', 'balanced', 'high', 'ultra'];
const statuses = [
  'UNKNOWN', 'INVALID_ARGUMENT', 'NOT_FOUND', 'ALREADY_EXISTS', 'IO',
  'DISPOSED', 'INVALID_OPERATION', 'CANCELLED', 'LIMIT_EXCEEDED', 'OUT_OF_MEMORY',
];
let addon;

function native() {
  if (addon) return addon;
  const { target, library } = require('./platform.cjs');
  const binaryPackage = `@vessel-storage/${target}`;
  const override = process.env.VESSEL_LIBRARY_PATH;
  try {
    const binary = override ? require.resolve('./build/Release/vessel.node') : require.resolve(binaryPackage);
    const loaded = require(binary);
    loaded.load(override ? path.resolve(override) : path.join(path.dirname(binary), library));
    addon = loaded;
    return addon;
  } catch (cause) {
    throw new Error(`Cannot load Vessel for ${target}. Install ${binaryPackage} with optional dependencies enabled.`, {
      cause,
    });
  }
}

class VesselError extends Error {
  constructor(status, options) {
    const kind = statuses[-status - 1] ?? 'UNKNOWN';
    super(`Vessel operation failed (${kind})`, options);
    this.name = 'VesselError';
    this.status = status;
    this.code = `ERR_VESSEL_${kind}`;
  }
}

function translate(error) {
  return typeof error?.status === 'number' ? new VesselError(error.status, { cause: error }) : error;
}

function text(value, name) {
  if (typeof value !== 'string' || value.includes('\0'))
    throw new TypeError(`${name} must be a string without NUL bytes`);
  return value;
}

function bool(value, name) {
  if (typeof value !== 'boolean') throw new TypeError(`${name} must be a boolean`);
  return value;
}

function enumValue(names, value, name) {
  const index = names.indexOf(value);
  if (index === -1) throw new TypeError(`Invalid ${name}: ${value}`);
  return index;
}

function options({ overwrite = false, compression: codec = 'none', level = 'balanced',
  fileName, extension, mimeType } = {}) {
  return [
    bool(overwrite, 'overwrite'),
    enumValue(compression, codec, 'compression'),
    enumValue(levels, level, 'compression level'),
    [fileName, extension, mimeType].map(value => value == null ? null : text(value, 'metadata')),
  ];
}

function metadata(result) {
  result.compression = compression[result.compression];
  result.contentHash = result.hash.slice(0, 64);
  return result;
}

class Upload {
  #handle;
  #native;

  constructor(api, store, key, settings) {
    this.#native = api;
    const { promise, resolve, reject } = Promise.withResolvers();
    this.#handle = api.begin(store, key, ...settings, (error, result) => {
      if (error) reject(translate(error));
      else resolve(metadata(result));
    });
    this.done = promise;
  }

  async write(buffer) {
    if (!Buffer.isBuffer(buffer) || types.isSharedArrayBuffer(buffer.buffer))
      throw new TypeError('Expected a Buffer with non-shared backing memory');
    const length = buffer.length;
    try {
      for (let offset = 0; offset < length; offset += chunkSize) {
        const count = Math.min(chunkSize, length - offset);
        if (this.#native.write(this.#handle, buffer, offset, count))
          await this.#native.drain(this.#handle);
      }
    } catch (error) {
      // The upload completion carries the underlying storage failure.
      if (error.status === -6 || error.status === -7 || error.status === -8) await this.done;
      throw translate(error);
    }
  }

  end(commit) {
    this.#native.end(this.#handle, commit);
  }
}

class Vessel {
  #native;
  #store;
  #closing;
  #pending = new Set();
  #uploads = new Set();

  constructor(token, api, store) {
    if (token !== construction) throw new TypeError('Use Vessel.open()');
    this.#native = api;
    this.#store = store;
  }

  static async open(directory, { enableMetadataCompression = false } = {}) {
    const location = text(directory, 'directory');
    if (!location.trim()) throw new TypeError('directory must not be empty');
    const metadataCompression = bool(enableMetadataCompression, 'enableMetadataCompression');
    const api = native();
    try {
      const store = await api.open(path.resolve(location), metadataCompression);
      return new Vessel(construction, api, store);
    } catch (error) {
      throw translate(error);
    }
  }

  #assertOpen() {
    if (this.#closing) throw new VesselError(-6);
  }

  async #run(operation, key) {
    this.#assertOpen();
    const pending = operation(this.#store, key);
    this.#pending.add(pending);
    try { return await pending; }
    catch (error) { throw translate(error); }
    finally { this.#pending.delete(pending); }
  }

  #start(key, settings) {
    this.#assertOpen();
    const upload = new Upload(this.#native, this.#store, key, settings);
    this.#uploads.add(upload);
    const remove = () => this.#uploads.delete(upload);
    upload.done.then(remove, remove);
    return upload;
  }

  async put(key, buffer, settings) {
    const upload = this.#start(text(key, 'key'), options(settings));
    try {
      await upload.write(buffer);
      upload.end(true);
    } catch (error) {
      upload.end(false);
      await upload.done.catch(() => {});
      throw error;
    }
    return upload.done;
  }

  createReadStream(key, { signal, highWaterMark = chunkSize } = {}) {
    this.#assertOpen();
    text(key, 'key');
    const api = this.#native;
    const open = () => this.#run(api.openReader, key);
    let handle;
    let reading;
    return new Readable({
      signal, highWaterMark,
      construct(callback) {
        open().then(reader => { handle = reader; callback(); }, callback);
      },
      read(size) {
        const buffer = Buffer.allocUnsafeSlow(Math.max(1, Math.min(size, chunkSize)));
        reading = api.read(handle, buffer);
        reading.then(count => {
          reading = undefined;
          if (this.destroyed) return;
          if (count === 0) this.push(null);
          else if (count === buffer.length) this.push(buffer);
          else {
            buffer.fill(0, count);
            this.push(buffer.subarray(0, count));
          }
        }, error => {
          reading = undefined;
          this.destroy(translate(error));
        });
      },
      destroy(error, callback) {
        const close = async () => {
          await reading?.catch(() => {});
          if (handle) await api.closeReader(handle);
        };
        close().then(() => callback(error), failure => callback(error ?? translate(failure)));
      },
    });
  }

  createWriteStream(key, { signal, highWaterMark = chunkSize, ...settings } = {}) {
    this.#assertOpen();
    text(key, 'key');
    const writeOptions = options(settings);
    const start = () => this.#start(key, writeOptions);
    let upload;
    return new Writable({
      signal, highWaterMark,
      construct(callback) {
        try {
          upload = start();
          upload.done.catch(error => this.destroy(error));
          callback();
        } catch (error) { callback(error); }
      },
      write(buffer, encoding, callback) {
        upload.write(buffer).then(() => callback(), callback);
      },
      final(callback) {
        upload.end(true);
        upload.done.then(() => callback(), callback);
      },
      destroy(error, callback) {
        if (!upload) return callback(error);
        upload.end(false);
        upload.done.then(() => callback(error), failure => {
          callback(error ?? (failure.status === -8 ? null : failure));
        });
      },
    });
  }

  async getMetadata(key) {
    return metadata(await this.#run(this.#native.metadata, text(key, 'key')));
  }

  async delete(key) {
    return this.#run(this.#native.delete, text(key, 'key'));
  }

  async keys(prefix = '') {
    return this.#run(this.#native.keys, text(prefix, 'prefix'));
  }

  close() {
    if (!this.#closing) {
      const uploads = [...this.#uploads];
      this.#closing = Promise.allSettled([...this.#pending, ...uploads.map(upload => upload.done)])
        .then(() => this.#native.close(this.#store))
        .catch(error => { throw translate(error); });
      for (const upload of uploads) upload.end(false);
    }
    return this.#closing;
  }

  [Symbol.asyncDispose]() {
    return this.close();
  }
}

module.exports = { Vessel, VesselError };
