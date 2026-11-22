import { Buffer } from 'node:buffer';
import { Readable, Writable } from 'node:stream';
import { Vessel, VesselError, type ObjectMetadata, type WriteOptions } from 'vessel-storage';

const vessel = await Vessel.open('./store');
const options: WriteOptions = { compression: 'zstd', overwrite: true };
const metadata: ObjectMetadata = await vessel.put('key', Buffer.from('data'), options);
const size: bigint = metadata.originalSize;
const source: Readable = vessel.createReadStream('key');
const destination: Writable = vessel.createWriteStream('copy', { signal: AbortSignal.timeout(1000) });
const error: VesselError = new VesselError(-3);
await vessel[Symbol.asyncDispose]();
// @ts-expect-error The public API only accepts binary buffers.
await vessel.put('key', 'data');
void [size, source, destination, error];
