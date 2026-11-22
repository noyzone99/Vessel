# Vessel for Node.js

Vessel stores binary objects by key in a local directory. The `vessel-storage` package exposes the `Vessel` class to JavaScript and TypeScript on Node.js 24 and later.

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

See the [Node.js guide](../../Docs/UserGuide/NodeJS/README.md) for setup and local builds, and the [API reference](../../Docs/ApiReferences/NodeJS/README.md) for the public contract.
