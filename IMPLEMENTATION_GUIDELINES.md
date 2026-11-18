# Implementation guidelines for filesystems and virtual disk formats

This guide is for contributors, including AI coding agents, adding or substantially extending a storage format in LTRData.DiscUtils. Its purpose is to avoid rediscovering the architecture and to make implementations usable by existing DiscUtils consumers, not merely by a format-specific example application.

Use the current checkout of this fork as the authority for API signatures and behavior. Older versions of DiscUtils, external examples and existing legacy implementations may not reflect the preferred design. Use the references below as starting points, not as code to copy indiscriminately.

**Required** items are integration and correctness expectations for the capabilities undertaken. **Recommended** items should be implemented when applicable or explicitly accounted for in the proposal. A useful read-only implementation is welcome; creation, modification, recovery and optional interfaces can be separate contributions. Do not claim unsupported capabilities.

## 1. Before implementing

1. Identify the format specification, versions, variants, incompatible features, and representative independent implementations or tools. Record authoritative references and licensing/provenance for any reused material.
2. Check existing issues, pull requests and format packages for overlapping work. Distinguish an ordinary filesystem, an archive filesystem view, a virtual disk, and a volume/partition mapping layer.
3. State the intended capabilities: read, create, modify existing, metadata, sparse/allocation mapping, compression/encryption, multi-device or parent layers, recovery and async I/O as relevant. Identify features outside scope and how they will be detected.
4. Read the applicable shared contracts and one or two reference implementations below before adding new abstractions. Explain any proposed change to shared APIs separately; avoid unrelated refactoring.
5. Plan independent fixtures, malformed input tests, integration through generic APIs and target-framework validation before writing the parser.

## 2. Repository and architecture map

| Area | Starting point |
| --- | --- |
| Common filesystem and disk contracts, volume and partition discovery | [DiscUtils.Core](Library/DiscUtils.Core) |
| Generic directory-entry, file and directory model | [Core/Vfs](Library/DiscUtils.Core/Vfs) |
| Sparse streams, buffers, binary parsing, stream composition | [DiscUtils.Streams](Library/DiscUtils.Streams) |
| Read-only Unix filesystems and extent mapping | [Ext](Library/DiscUtils.Ext), [XFS](Library/DiscUtils.Xfs), [Btrfs](Library/DiscUtils.Btrfs) |
| Writable filesystems | [FAT](Library/DiscUtils.Fat), [exFAT](Library/DiscUtils.ExFat), [NTFS](Library/DiscUtils.Ntfs) |
| Read-only filesystem with a separate image builder | [SquashFS](Library/DiscUtils.SquashFs), [ISO 9660](Library/DiscUtils.Iso9660) |
| Archive-backed filesystem views | [VirtualFileSystem](Library/DiscUtils.VirtualFileSystem) |
| Relatively simple fixed/dynamic virtual disks | [VDI](Library/DiscUtils.Vdi) |
| Virtual disks with parent chains, sparse content and creation | [VHD](Library/DiscUtils.Vhd), [VHDX](Library/DiscUtils.Vhdx), [VMDK](Library/DiscUtils.Vmdk) |
| Shared and format-specific tests | [LibraryTests](Tests/LibraryTests), [RegistrationTests](Tests/RegistrationTests), [NativeAotSmoke](Tests/NativeAotSmoke) |

A typical composition is:

    backing stream(s) -> VirtualDisk.Content -> partition/volume stream
                      -> DiscFileSystem -> OpenFile -> SparseStream

A filesystem normally receives a stream starting at the filesystem itself, not a whole partitioned disk. A virtual disk exposes logical sector content, not the bytes of its container headers. Partition and volume discovery belong to their existing layers. Keep the layers independently usable and composable; do not introduce host filesystem or OS-mount dependencies into format readers.

## 3. Filesystem implementations

### 3.1 Common object model (required)

Start with [IFileSystem](Library/DiscUtils.Core/IFileSystem.cs) and [DiscFileSystem](Library/DiscUtils.Core/DiscFileSystem.cs). Use DiscUtils file and directory information objects (DiscFileInfo, DiscDirectoryInfo, DiscFileSystemInfo), not host filesystem equivalents or a parallel public object model.

For a filesystem with a directory-entry/inode-style model, prefer evaluating the existing VFS infrastructure first:

- A public format-specific filesystem derived from VfsFileSystemFacade, forwarding applicable optional interfaces to the implementation.
- An internal VfsFileSystem or VfsReadOnlyFileSystem with the appropriate directory-entry, file, directory and context types.
- VfsDirEntry, IVfsFile, IVfsDirectory and, where appropriate, IVfsSymlink and IVfsFileWithStreams.

Ext is a useful starting point for this arrangement. Direct derivation from DiscFileSystem or ReadOnlyDiscFileSystem is also appropriate when the format is a poor fit for the VFS model; FAT is an example of a direct implementation. Do not introduce a new VFS abstraction solely for one format.

### 3.2 Basic filesystem behavior (required)

Implement and test the applicable common contract, not just a custom list/extract API:

- Root and nested directory navigation, existence checks, file/directory distinction, file information, file length, attributes and opening files using the common FileMode/FileAccess semantics.
- Lazy enumeration of files, directories and mixed entries, including search patterns and recursion. Prefer the existing shared path and wildcard logic where suitable. Respect the on-disk case sensitivity and name/encoding rules; do not use host path semantics.
- UTC creation, access and modification times as available; distinguish absent or unrepresentable metadata from genuine values and document any necessary fallback. Reuse base-class conversions and format-specific time utilities.
- FriendlyName, volume label and volume identity where meaningful; Size, UsedSpace and AvailableSpace in bytes. Implement reliable space accounting when possible. If used/free accounting is not supported, report SupportsUsedAvailableSpace accordingly rather than returning fictitious totals.
- Return SparseStream from OpenFile and stream content on demand. Support seek, partial reads, EOF, and independent file handles. Use the target filesystem's semantics for symlinks and hard links, including cycle handling and resolution relative to the image, never the host.
- Set CanWrite according to actual implementation and stream access. A read-only open must not modify the source, even if the supplied stream is writable. Unsupported mutations must fail explicitly.
- Implement or document relevant options and FileSystemParameters, including name encoding where applicable. Make ownership and disposal behavior explicit.

### 3.3 Optional standard interfaces (recommended when semantically applicable)

Assess these interfaces individually. Consumers discover capabilities by interface checks, so implement them on the public filesystem object rather than only on an internal VFS class. Do not implement a capability with dummy values or blanket NotImplementedException stubs.

| Contract | Purpose / reference |
| --- | --- |
| [IUnixFileSystem](Library/DiscUtils.Core/IUnixFileSystem.cs) | UnixFileSystemInfo, UID/GID, permissions, file type and other representable Unix metadata. See Ext, Btrfs, XFS. |
| [IAllocationExtentsFileSystem](Library/DiscUtils.Core/IAllocationExtentsFileSystem.cs) | PathToExtents: physical allocation ranges in the underlying filesystem stream. See Btrfs and XFS for nontrivial allocation models. |
| [IClusterBasedFileSystem](Library/DiscUtils.Core/IClusterBasedFileSystem.cs) | Extends allocation extents with sector/cluster geometry, conversions, cluster ranges and allocated cluster counts. See Ext and FAT. |
| [IFileSystemWithClusterMap](Library/DiscUtils.Core/IFileSystemWithClusterMap.cs) | Full ClusterMap with allocation roles and associations. See NTFS. |
| [IDosFileSystem](Library/DiscUtils.Core/IDosFileSystem.cs) | DOS/Windows-style short names, standard information, file IDs and volume metadata. See FAT. |
| [IFileSystemWithAltStreams](Library/DiscUtils.Core/IFileSystemWithAltStreams.cs) | Named alternate data streams. |
| [IWindowsFileSystem](Library/DiscUtils.Core/IWindowsFileSystem.cs) | DOS metadata, alternate streams, security descriptors, reparse points and hard-link queries. See NTFS. |
| [IFileSystemWithEnumerationOptions](Library/DiscUtils.Core/IFileSystemWithEnumerationOptions.cs) | Framework-conditional EnumerationOptions overloads; respect the current contract and existing shared behavior. |

Allocation mapping is particularly valuable to forensic, disk cloning and storage-analysis applications. Implement it whenever the format offers a reliable mapping:

- PathToExtents reports byte positions in the underlying filesystem stream, not offsets in the logical file or physical offsets in an enclosing VHD.
- Dedicated allocation, logical file length and stored/compressed byte count are different concepts. Do not derive allocated cluster counts solely from logical length.
- Respect holes, unwritten extents, inline/resident data, compressed extents, shared blocks, fragmentation and multiple devices where applicable. Document representation and limitations. An empty extent set is not a substitute for an unknown mapping.
- Use the simpler allocation-extent interface if a meaningful global fixed-size cluster model cannot be exposed. Do not fabricate a sector/cluster model to satisfy an interface.

### 3.4 Creation, modification and recovery (when in scope)

Distinguish building a new filesystem from formatting a writable stream and from modifying existing filesystems. A builder may implement [IFileSystemBuilder](Library/DiscUtils.Core/IFileSystemBuilder.cs) where applicable, exposing its existing generic file/directory and metadata inputs. See the SquashFS builder. A builder does not imply writable reader support.

For writable filesystems, implement the relevant create, open, truncate, append, extend, overwrite, rename, delete, and metadata semantics. Validate allocation/free-space accounting, reuse of freed blocks, directory updates and persistence after reopening. Honor read-only flags and requested access on all mutation paths, including streams already returned to callers.

Treat dirty, journaled, hibernated or otherwise inconsistent volumes conservatively. Do not clear dirty flags, skip required recovery, or silently write back repaired metadata. Refuse unsafe operations; clearly distinguish in-memory recovery from persistent recovery. Persistent recovery must have explicit semantics, exclusive/offline access where required, and tested write ordering. See [NTFS journal durability](docs/ntfs-journal-replay-durability.md) for one format-specific example, not a universal algorithm.

## 4. Virtual disk / image container implementations

### 4.1 Class responsibilities (required)

Start with [VirtualDisk](Library/DiscUtils.Core/VirtualDisk.cs), [VirtualDiskLayer](Library/DiscUtils.Core/VirtualDiskLayer.cs), [VirtualDiskExtent](Library/DiscUtils.Core/VirtualDiskExtent.cs), and [VirtualDiskFactory](Library/DiscUtils.Core/Internal/VirtualDiskFactory.cs).

Follow the separation used by VDI, VHD and VHDX where appropriate:

| Typical class | Responsibility |
| --- | --- |
| Disk : VirtualDisk | Complete logical disk, capacity, logical sector size, geometry, disk type/parameters, Content, Layers and differencing creation where supported. |
| DiskImageFile : VirtualDiskLayer | One image layer, metadata, sparse/parent information, backing extents and OpenContent(parent, ownsParent). |
| ContentStream / DiskStream : SparseStream or MappedStream | Translate logical disk byte offsets to the underlying image data; implement allocation, zero/parent behavior and I/O. |
| DiskFactory : VirtualDiskFactory | Format/extension registration, supported variants, generic open/create, layer opening and advertised type information. |
| DiskBuilder : DiskImageBuilder | Optional conversion/build path from logical source content to a new image representation. |

These are responsibilities, not mandatory filenames or class counts. A format with several extent files may require additional classes.

Expose logical disk content through the common disk APIs so partition detection, VolumeManager and filesystem readers work without format-specific knowledge. Capacity is the logical byte length; BlockSize is the logical sector size, not the container allocation block size. Support appropriate non-512-byte sectors; distinguish logical sectors, physical sectors, allocation units and file-offset units. Derive or report geometry according to actual format semantics.

Use FileLocator and existing stream-based entry points rather than requiring host file paths. Where a format needs companion files or parent layers, document restrictions of the single-stream API. Honor FileAccess, supported variants, and ownership of backing streams and supplied parents. Opening an existing image must not create or truncate it on a failed probe.

### 4.2 Sparse data, parent chains and writes

- Correctly distinguish stored data, implicit zeroes, explicit zero overrides and inherited parent data. An unallocated child block is not always zero. A zero operation on a child must not accidentally expose nonzero parent bytes.
- Validate the parent chain, identity, capacity, sector sizes and layout compatibility. Reject missing, mismatched, cyclic or unsupported chains; do not silently substitute an empty parent.
- Keep parents read-only during child writes. Merge, resize, differencing creation and physical space reclamation are independent features, not implicit consequences of write support.
- Support reads and writes across allocation boundaries, partial sectors where the stream API permits, and correct read-modify-write of neighboring data. Never expose stale data from newly allocated blocks.
- Implement allocation metadata updates and ordering so a reopened image sees the correct data. Do not advertise crash consistency or durability beyond tested guarantees.
- Where supported, expose Clear/ClearAsync with efficient zeroing/deallocation, respecting the sparse stream contract, position, range, parent inheritance and format-specific durability constraints. Physical allocation reclamation is optional.

## 5. Streams, binary parsing and performance

### 5.1 Shared infrastructure (required)

Read [SparseStream](Library/DiscUtils.Streams/SparseStream.cs) before implementing a content stream. Use [MappedStream](Library/DiscUtils.Streams/MappedStream.cs) when its physical mapping contract is applicable. Review the buffer abstraction ([IBuffer](Library/DiscUtils.Streams/Buffer/IBuffer.cs), Buffer, BufferStream, IMappedBuffer) and existing SubStream, ZeroStream, SparseMemoryStream, AligningStream, Block helpers and stream wrappers.

Keep coordinate systems distinct:

| API | Coordinates / meaning |
| --- | --- |
| SparseStream.Extents / GetExtentsInRange | Logical ranges backed by data in that stream's address space; holes read as zero. |
| MappedStream.MapContent | Mapping of a logical range to underlying storage according to the mapped-stream contract. |
| IAllocationExtentsFileSystem.PathToExtents | Physical byte ranges in the stream on which the filesystem is opened. |
| PathToClusters | Dedicated cluster ranges in the filesystem's own allocation model. |

Report sparse extents conservatively and accurately, including inherited data where relevant. Reuse [StreamExtent](Library/DiscUtils.Streams/StreamExtent.cs) for range operations. Prefer efficient range queries over scanning every allocation unit or filtering all extents on each request. Test callers that use extents to skip holes during sparse copying.

Honor Stream semantics: offset/count, Position, Length, seek origin, short reads, EOF, zero-length I/O, capabilities, disposal and errors. Do not assume a single Read fills a buffer. Use explicit Ownership.None / Ownership.Dispose conventions when appropriate. Avoid closing caller-owned streams on success or failure and release owned resources on failed construction.

### 5.2 Efficient I/O and parsing (recommended)

- Prefer Span<byte>/ReadOnlySpan<byte> for synchronous parsing and Memory<byte>/ReadOnlyMemory<byte> for asynchronous I/O. Use current stream compatibility bases and avoid unnecessary copies between array, span and memory overloads.
- Use existing [StreamUtilities](Library/DiscUtils.Streams/Util/StreamUtilities.cs) (ReadExactly, ReadMaximum, ReadStruct and async equivalents), endian, encoding and math utilities. Consider [IByteArraySerializable](Library/DiscUtils.Streams/IByteArraySerializable.cs) for structured on-disk records. Account for byte order, field width and on-disk packing explicitly.
- Use 64-bit offsets and sizes, checked conversions/arithmetic where input can overflow, bounded stack allocations, and pooled/reusable buffers for hot paths where beneficial. Clear bytes when required for correctness or confidentiality. Never expose uninitialized or stale buffers.
- Avoid reading whole disks, files, or unbounded metadata tables into memory. Parse lazily or in bounded blocks, cache only when useful, and permit large/sparse images on 32-bit and 64-bit runtimes.
- Review existing compression code in [Core/Compression](Library/DiscUtils.Core/Compression), stream utilities and format-specific codec dependencies before implementing another codec. Document dependency/platform restrictions.

### 5.3 Async, concurrency and flushing (required where applicable)

Implement real asynchronous content reads/writes where backing storage supports them, especially for virtual disk and file content hot paths. Support Memory-based async overloads, CancellationToken propagation, and identical sync/async semantics. Do not use Task.Run or synchronous I/O disguised as async merely to satisfy a signature; document unavoidable synchronous fallback paths.

Never advertise IsThreadSafe unless shared stream position, metadata, caches and enumeration/stream lifetime are protected across all relevant operations. Independent handles must not corrupt each other's state. Review SparseStream.Synchronized and VirtualDisk.AsSynchronized; wrapping one shared cursor does not make a larger multi-step filesystem operation atomic. Design for safe positioned I/O or coordinated access without excessive locking. Document the concurrency contract.

Writable streams and wrappers must propagate Flush(), FlushAsync and [SparseStream.Flush(bool flushToDisk)](Library/DiscUtils.Streams/SparseStream.cs) through all relevant layers, using existing helpers such as FlushWrappedStream. Distinguish ordinary buffer flush from a stronger durability request; the backing storage must actually support the latter to make a durable-write guarantee. Test propagation and any metadata/data ordering that the format relies upon.

## 6. Untrusted input and failure policy (required)

Treat every field in an image as untrusted, even if the format is usually used on local disks. Validate magic, versions, feature flags, checksums where required, table lengths, offsets, block sizes, counts, bounds and cross-references before using them. Bound traversal of trees, chains, symlinks and decompression; detect cycles and non-progress. Prevent unchecked arithmetic, oversized allocations, reads outside the intended stream and excessive recursion/work from malformed inputs.

Detection is a lightweight, non-mutating probe. Return no match for ordinary unrelated or too-short streams; avoid hiding unrelated I/O failures. Opening performs deeper validation. Reject unknown incompatible features and unsupported write-critical features before exposing unsafe operations. Distinguish a corrupt/truncated image, an unsupported format feature, an access failure and an invalid caller argument using appropriate existing exception conventions. Never silently return zero data or an incomplete directory because parsing failed.

Do not execute external programs, mount filesystems, or follow arbitrary host paths or network locations based on image metadata. Formats with parents, external extents or companion files should use the existing FileLocator abstraction and caller-authorized resolution policy; document the trust boundary and do not allow image metadata to bypass it. Explicitly requested external fixtures/tools belong in tests, not normal format parsing.

## 7. Discovery, packaging and compatibility (required)

- Use a focused Library/DiscUtils.<Format> project and the DiscUtils.<Format> namespace for a new independently consumable format. Inherit shared build/package settings; NuGet package IDs use LTRData.DiscUtils.<Format>. Update [DiscUtils.slnx](DiscUtils.slnx) and appropriate project references, including the relevant aggregate package(s).
- Filesystem discovery: implement a VfsFileSystemFactory with the VfsFileSystemFactory attribute and return appropriate VfsFileSystemInfo handles for opening detected volumes/streams. Use Ext/FileSystemFactory.cs as a starting point.
- Disk discovery: implement and attribute VirtualDiskFactory as in VDI/DiskFactory.cs or another suitable format. Advertise only supported variants and correct type/extension metadata; wire supported creation/build APIs into the factory.
- Built-in registration is generated at build time. Add the appropriate explicit Format namespace's Formats.Register() call to the relevant composition helper(s), and check both individual and aggregate registration scope. Do not add assembly scanning, module initializers or reflection-only registration for a built-in format. Read [format registration and Native AOT](docs/native-aot-format-registration.md) for the authoritative mechanism.
- Preserve supported target frameworks from [Library/Directory.Build.props](Library/Directory.Build.props). Use existing compatibility APIs and conditional compilation where a new BCL API is not available on older targets. Avoid silently raising the minimum framework or requiring a specific OS or architecture.
- Follow repository-level build configuration and style in .editorconfig and Directory.Build.props/targets. Keep public APIs small, document public members and format assumptions, and use existing shared utilities before adding dependencies.
- Add the package README explaining capabilities, supported versions/variants, usage, required dependencies, limitations and read/create/modify distinctions; update the root README capability matrix and relevant aggregate-package READMEs.

## 8. Testing and review evidence (required)

Place format tests below [Tests/LibraryTests](Tests/LibraryTests). Reuse shared filesystem and disk test patterns when applicable, and register suitable new instances with shared test data sources where practical. Review the current test project, shared test build properties and CI workflows before choosing test commands or target frameworks.

Include applicable coverage for:

- Independent, reproducible fixtures created by established implementations (for example mkfs/mksquashfs or qemu-img), with creation commands, tool versions, provenance and expected metadata/content. Self-generated round trips are useful but insufficient by themselves.
- Direct open and generic detection/registration; unrelated/truncated input, unsupported variants and incorrect or ambiguous signatures. Test relevant aggregate registration and Native AOT behavior.
- Empty images, root/nested/large directories, Unicode and case rules, file and metadata semantics, sparse/fragmented/inline/compressed files, allocation ranges, and large offsets beyond 2 GiB/4 GiB as applicable.
- Small and unaligned reads, short-reading backing streams, boundaries, EOF, seeks, sync/async parity, cancellation, independent handles, and ownership/disposal on success and failure.
- Fixed/dynamic/differencing variants where supported, cross-block reads/writes, inherited versus zero regions, sector-size variation, logical hashes against independent references.
- For creation or modification: reopen and validate persisted data, metadata and allocation, test an independent reader/checker, and exercise read-only access, flush propagation and interruption/failure cases where ordering is important.
- Malformed, adversarial and truncated metadata; avoid hangs, out-of-bounds I/O, uncontrolled allocations and fabricated successful results.

Keep checked-in test fixtures compact and legally distributable. Where external tools are optional, distinguish genuinely unavailable tools or capabilities from test failures. Keep core tests runnable without platform-specific dependencies where feasible. Report exact commands, frameworks/OSes tested and any skipped coverage; passing one framework does not establish compatibility with all others.

## 9. Pull request checklist

The PR description should summarize the intended scope, specification sources, design references and deviations from this guide. Include a small capability matrix (implemented, unsupported, not applicable or deferred) for read/create/modify, variants, relevant interfaces, sparse/allocation mapping, async I/O, concurrency and recovery. State how unsupported features are handled, not merely that they are missing.

- [ ] Uses the common filesystem/virtual disk/stream APIs and existing shared infrastructure.
- [ ] Evaluates relevant standard interfaces and documents omitted capabilities.
- [ ] Correct capabilities, ownership, read-only behavior, sparse semantics and error handling.
- [ ] Generic discovery and explicit format registration work.
- [ ] Package, aggregate references, solution and documentation updated as applicable.
- [ ] Independent fixtures and regression tests cover the claimed scope; actual test results reported.
- [ ] No unrelated API/architecture changes or unsupported capability claims.

An incomplete but correct, explicitly scoped contribution is preferable to a broad implementation with placeholders, silently incorrect results or untested promises. For changes to common contracts, or designs that cannot fit existing abstractions, describe the issue and obtain maintainer agreement before making large cross-cutting changes.
