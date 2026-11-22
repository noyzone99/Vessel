'use strict';

const { execFileSync } = require('node:child_process');
const { copyFileSync, mkdirSync, writeFileSync } = require('node:fs');
const path = require('node:path');
const { arch, platform, libc, target, library } = require('../platform.cjs');
const manifest = require('../package.json');

const [nativeLibrary, outputDirectory, ...options] = process.argv.slice(2);
const name = `@vessel-storage/${target}`;
if (!nativeLibrary || !outputDirectory || !manifest.optionalDependencies[name] ||
    options.some(option => option !== '--verify')) {
  throw new Error('Usage: npm run pack:local -- <Vessel library> <output directory> [--verify]');
}
for (const version of Object.values(manifest.optionalDependencies)) {
  if (version !== manifest.version) throw new Error('Vessel npm packages must share one version');
}

const source = path.resolve(__dirname, '..');
const output = path.resolve(outputDirectory);
const wrapper = path.join(output, 'wrapper');
const binaries = path.join(output, 'binaries', target);
mkdirSync(wrapper, { recursive: true });
mkdirSync(binaries, { recursive: true });

const license = path.resolve(source, '../../LICENSE');
for (const file of ['package.json', 'README.md', ...manifest.files.filter(file => file !== 'LICENSE')])
  copyFileSync(path.join(source, file), path.join(wrapper, file));
copyFileSync(license, path.join(wrapper, 'LICENSE'));
copyFileSync(license, path.join(binaries, 'LICENSE'));
copyFileSync(path.resolve(nativeLibrary), path.join(binaries, library));
copyFileSync(path.join(source, 'build/Release/vessel.node'), path.join(binaries, 'vessel.node'));
writeFileSync(path.join(binaries, 'package.json'), JSON.stringify({
  name, version: manifest.version, license: manifest.license, repository: manifest.repository,
  engines: manifest.engines, os: [platform], cpu: [arch], ...(libc && { libc: [libc] }),
  main: './vessel.node', files: ['vessel.node', library, 'LICENSE'],
}, null, 2) + '\n');

const npm = (...args) => execFileSync(process.execPath, [process.env.npm_execpath, ...args], { cwd: output });
const pack = directory => {
  const [result] = JSON.parse(npm('pack', '--json', directory));
  console.log(result.filename);
  return path.join(output, result.filename);
};
const wrapperTarball = pack(wrapper);
const binaryTarball = pack(binaries);

if (options.includes('--verify')) {
  const consumer = path.join(output, 'consumer');
  mkdirSync(consumer, { recursive: true });
  writeFileSync(path.join(consumer, 'package.json'), '{"private":true}\n');
  npm('install', '--offline', '--ignore-scripts', '--no-audit', '--prefix', consumer,
    wrapperTarball, binaryTarball);
  execFileSync(process.execPath, ['--test', path.join(source, 'test/storage.test.cjs')], {
    stdio: 'inherit',
    env: { ...process.env, VESSEL_LIBRARY_PATH: '', VESSEL_NODE_CONSUMER: consumer },
  });
}
