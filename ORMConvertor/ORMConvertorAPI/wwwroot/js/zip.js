/*
 * A ZIP archive read in the browser by own code (decision 136): the end record, the central
 * directory and the local headers, entries stored as they are or deflated, and the ZIP64
 * extension. The browser inflates through its own DecompressionStream, so no library is
 * vendored (decision 032 f). The archive is read through Blob slices, so only the entries
 * actually taken are ever in memory.
 *
 * The layout follows the PKWARE specification (APPNOTE.TXT); every number in it is little
 * endian.
 */

const END_OF_CENTRAL_DIRECTORY = 0x06054b50;
const ZIP64_END_LOCATOR = 0x07064b50;
const ZIP64_END_OF_CENTRAL_DIRECTORY = 0x06064b50;
const CENTRAL_DIRECTORY_HEADER = 0x02014b50;
const LOCAL_FILE_HEADER = 0x04034b50;
const ZIP64_EXTRA_FIELD = 0x0001;

const END_RECORD_LENGTH = 22;
const MAX_COMMENT_LENGTH = 0xffff;
const CENTRAL_HEADER_LENGTH = 46;
const LOCAL_HEADER_LENGTH = 30;

const STORED = 0;
const DEFLATED = 8;
const ENCRYPTED_FLAG = 0x0001;

const decoder = new TextDecoder();

async function viewOf(blob, start, end) {
  return new DataView(await blob.slice(start, end).arrayBuffer());
}

const u64 = (view, at) => Number(view.getBigUint64(at, true));

/*
 * The end record is the last structure of the file, followed only by a comment of at most
 * 64 KiB, so it is sought backwards through that tail.
 */
function findEndRecord(tail) {
  for (let at = tail.byteLength - END_RECORD_LENGTH; at >= 0; at--) {
    if (
      tail.getUint32(at, true) === END_OF_CENTRAL_DIRECTORY &&
      at + END_RECORD_LENGTH + tail.getUint16(at + 20, true) <= tail.byteLength
    ) {
      return at;
    }
  }
  return -1;
}

/*
 * Where the central directory lies and how many entries it has. A field that is all ones
 * says the true value is in the ZIP64 end record, which a locator just before the end
 * record points at.
 */
async function centralDirectory(blob) {
  const tailStart = Math.max(0, blob.size - END_RECORD_LENGTH - MAX_COMMENT_LENGTH);
  const tail = await viewOf(blob, tailStart, blob.size);
  const at = findEndRecord(tail);
  if (at < 0) throw new Error("it has no central directory, so it is not a ZIP archive");
  if (tail.getUint16(at + 4, true) !== 0 || tail.getUint16(at + 6, true) !== 0) {
    throw new Error("it is split across several files");
  }

  let count = tail.getUint16(at + 10, true);
  let size = tail.getUint32(at + 12, true);
  let offset = tail.getUint32(at + 16, true);
  if (count === 0xffff || size === 0xffffffff || offset === 0xffffffff) {
    const locatorStart = tailStart + at - 20;
    const locator = locatorStart >= 0 ? await viewOf(blob, locatorStart, locatorStart + 20) : null;
    if (!locator || locator.byteLength < 20 || locator.getUint32(0, true) !== ZIP64_END_LOCATOR) {
      throw new Error("its ZIP64 end record is missing");
    }
    const endStart = u64(locator, 8);
    const end = await viewOf(blob, endStart, endStart + 56);
    if (end.byteLength < 56 || end.getUint32(0, true) !== ZIP64_END_OF_CENTRAL_DIRECTORY) {
      throw new Error("its ZIP64 end record is damaged");
    }
    count = u64(end, 32);
    size = u64(end, 40);
    offset = u64(end, 48);
  }

  if (offset + size > blob.size) throw new Error("its central directory lies past the end of the file");
  return { count, view: await viewOf(blob, offset, offset + size) };
}

/*
 * The ZIP64 extra field carries, in this order, only those of the uncompressed size, the
 * compressed size and the local header offset whose 32-bit field in the header is all ones.
 */
function applyZip64(view, start, length, entry) {
  let at = start;
  const end = start + length;
  for (const key of ["uncompressedSize", "compressedSize", "localOffset"]) {
    if (entry[key] !== 0xffffffff) continue;
    if (at + 8 > end) throw new Error("its central directory is damaged");
    entry[key] = u64(view, at);
    at += 8;
  }
}

function problemOf(flags, method) {
  if (flags & ENCRYPTED_FLAG) return "it is encrypted";
  if (method !== STORED && method !== DEFLATED) {
    return `it is compressed with method ${method}, which the browser cannot unpack`;
  }
  return null;
}

/*
 * The data of an entry starts after its local header, whose name and extra field may differ
 * in length from the central directory's copy; the sizes are taken from the central
 * directory, because a local header followed by a data descriptor carries zeros.
 */
async function readEntry(blob, entry) {
  const header = await viewOf(blob, entry.localOffset, entry.localOffset + LOCAL_HEADER_LENGTH);
  if (header.byteLength < LOCAL_HEADER_LENGTH || header.getUint32(0, true) !== LOCAL_FILE_HEADER) {
    throw new Error("its local header is damaged");
  }
  const start =
    entry.localOffset + LOCAL_HEADER_LENGTH + header.getUint16(26, true) + header.getUint16(28, true);
  const data = blob.slice(start, start + entry.compressedSize);
  if (entry.method === STORED) return data;
  let inflate;
  try {
    inflate = new DecompressionStream("deflate-raw");
  } catch {
    throw new Error("this browser cannot inflate it");
  }
  try {
    return await new Response(data.stream().pipeThrough(inflate)).blob();
  } catch (error) {
    // Browsers word a failed inflate differently, some with no message at all.
    throw new Error(error?.message ? `its compressed data is damaged (${error.message})` : "its compressed data is damaged");
  }
}

/**
 * The entries of a ZIP archive in the order of its central directory:
 * { path, directory, problem, read }. `path` uses "/" whatever the archiver wrote;
 * `problem` says why the entry cannot be unpacked (encrypted, an unknown method), else null;
 * `read()` resolves to the entry's content as a Blob. Throws, with a sentence that completes
 * "the archive cannot be read: ...", when the archive itself cannot be read.
 */
export async function readZip(blob) {
  const { count, view } = await centralDirectory(blob);
  const entries = [];
  let at = 0;
  for (let index = 0; index < count; index++) {
    if (at + CENTRAL_HEADER_LENGTH > view.byteLength || view.getUint32(at, true) !== CENTRAL_DIRECTORY_HEADER) {
      throw new Error("its central directory is damaged");
    }
    const flags = view.getUint16(at + 8, true);
    const method = view.getUint16(at + 10, true);
    const nameLength = view.getUint16(at + 28, true);
    const extraLength = view.getUint16(at + 30, true);
    const commentLength = view.getUint16(at + 32, true);
    const nameStart = at + CENTRAL_HEADER_LENGTH;
    const extraStart = nameStart + nameLength;
    const next = extraStart + extraLength + commentLength;
    if (next > view.byteLength) throw new Error("its central directory is damaged");

    const entry = {
      method,
      compressedSize: view.getUint32(at + 20, true),
      uncompressedSize: view.getUint32(at + 24, true),
      localOffset: view.getUint32(at + 42, true),
    };
    for (let field = extraStart; field + 4 <= extraStart + extraLength; ) {
      const id = view.getUint16(field, true);
      const length = view.getUint16(field + 2, true);
      if (id === ZIP64_EXTRA_FIELD) applyZip64(view, field + 4, length, entry);
      field += 4 + length;
    }

    // Read as UTF-8 even without the archiver's UTF-8 flag: a source file's name is ASCII as
    // a rule, which reads the same in every encoding, and the name is only a label (066).
    const path = decoder
      .decode(new Uint8Array(view.buffer, view.byteOffset + nameStart, nameLength))
      .replaceAll("\\", "/");
    entries.push({
      path,
      directory: path.endsWith("/"),
      problem: problemOf(flags, method),
      read: () => readEntry(blob, entry),
    });
    at = next;
  }
  return entries;
}
