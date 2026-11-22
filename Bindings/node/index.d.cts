import type { Buffer } from 'node:buffer';
import type { Readable, Writable } from 'node:stream';

export type Compression = 'none' | 'zstd' | 'lz4';
export type CompressionLevel = 'fastest' | 'balanced' | 'high' | 'ultra';

export interface OpenOptions {
  enableMetadataCompression?: boolean;
}

export interface StreamOptions {
  highWaterMark?: number;
  signal?: AbortSignal;
}

export interface WriteOptions {
  overwrite?: boolean;
  compression?: Compression;
  level?: CompressionLevel;
  fileName?: string | null;
  extension?: string | null;
  mimeType?: string | null;
}

export interface ObjectMetadata {
  readonly key: string;
  readonly hash: string;
  readonly contentHash: string;
  readonly etag: string;
  readonly originalSize: bigint;
  readonly compressedSize: bigint;
  readonly compression: Compression;
  readonly createdAt: Date;
  readonly fileName: string | null;
  readonly extension: string | null;
  readonly mimeType: string | null;
}

export class VesselError extends Error {
  constructor(status: number, options?: ErrorOptions);
  readonly status: number;
  readonly code: `ERR_VESSEL_${string}`;
}

export class Vessel {
  private constructor();
  static open(directory: string, options?: OpenOptions): Promise<Vessel>;
  put(key: string, buffer: Buffer, options?: WriteOptions): Promise<ObjectMetadata>;
  createReadStream(key: string, options?: StreamOptions): Readable;
  createWriteStream(key: string, options?: WriteOptions & StreamOptions): Writable;
  getMetadata(key: string): Promise<ObjectMetadata>;
  delete(key: string): Promise<boolean>;
  keys(prefix?: string): Promise<string[]>;
  close(): Promise<void>;
  [Symbol.asyncDispose](): Promise<void>;
}
