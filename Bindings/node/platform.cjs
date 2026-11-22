'use strict';

const { arch, platform } = process;
const libc = platform === 'linux'
  ? (process.report.getReport().header.glibcVersionRuntime ? 'glibc' : 'musl') : undefined;
const target = [platform, arch, libc].filter(Boolean).join('-');
const library = { win32: 'vessel.dll', darwin: 'libvessel.dylib', linux: 'libvessel.so' }[platform];

module.exports = { arch, platform, libc, target, library };
