import vesselStorage = require('vessel-storage');
import { Buffer } from 'node:buffer';

async function write(vessel: vesselStorage.Vessel): Promise<vesselStorage.ObjectMetadata> {
  return vessel.put('key', Buffer.from('data'), { compression: 'lz4' });
}

void write;
