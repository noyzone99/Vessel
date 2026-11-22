# Node.js user guide

## Setup

Use Node.js 24 or later. [Build the main and platform npm packages](../BuildFromSource.md#build-the-nodejs-packages), then install the two generated tarballs in your application. For example, from the repository root on Windows x64:

```sh
npm install ./artifacts/npm/vessel-storage-0.0.1-alpha.1.tgz ./artifacts/npm/vessel-storage-win32-x64-0.0.1-alpha.1.tgz
```

After registry publication, `npm install vessel-storage` will resolve the platform package through optional dependencies. Keep optional dependencies enabled.

## Write and read

Save this example as `example.mjs` and run `node example.mjs`.

```js
import { Buffer } from 'node:buffer';
import { Vessel } from 'vessel-storage';

const vessel = await Vessel.open('./store');
try {
  await vessel.put('hello', Buffer.from('Hello, Vessel!'), { overwrite: true });
  for await (const chunk of vessel.createReadStream('hello')) {
    process.stdout.write(chunk);
  }
} finally {
  await vessel.close();
}
```

For TypeScript, install `@types/node` for your Node.js major and use `module: "NodeNext"` so TypeScript follows the package exports. JavaScript CommonJS callers can use `const { Vessel } = require('vessel-storage')`.

## Stream files

```js
import { createReadStream, createWriteStream } from 'node:fs';
import { pipeline } from 'node:stream/promises';
import { Vessel } from 'vessel-storage';

const vessel = await Vessel.open('./store');
try {
  await pipeline(createReadStream('input.bin'),
    vessel.createWriteStream('archive/input', { compression: 'zstd', overwrite: true }));
  await pipeline(vessel.createReadStream('archive/input'), createWriteStream('output.bin'));
} finally {
  await vessel.close();
}
```

Use `getMetadata(key)` to read metadata, `keys(prefix)` to find objects and `delete(key)` to remove one. See the [API reference](../../ApiReferences/NodeJS/README.md) and [shared storage guide](../Storage.md).
