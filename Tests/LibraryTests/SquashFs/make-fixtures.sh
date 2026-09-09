#!/bin/sh
# Rebuilds the SquashFS test images with mksquashfs (squashfs-tools 4.5 or later), byte for byte:
#   extended-inodes.sqsh  sha256 b1ec8a8c921b67cf49f577bfb0f764acc3c148e1fdfc2c37a45ed3d0f183f38d
#   huge-sparse.sqsh      sha256 fecdc2128d7fa899fbc59669255ca16830bbf50bcc631fbbded83aac2219bb31
# Times are fixed at 0 and ownership at 0:0, so anyone can regenerate the files and compare.
set -e
work=$(mktemp -d)
mkdir -p "$work/ext/dir" "$work/huge"
cd "$work/ext"
printf 'hello' > small.txt
yes "a line of text that compresses well, over and over" | head -c 300000 > text.bin
dd if=/dev/zero of=sparse.bin bs=1048576 count=2 status=none
printf 'end' >> sparse.bin
printf 'twice' > hard.txt
ln hard.txt dir/hard2.txt
cd "$work/huge"
dd if=/dev/zero of=huge.bin bs=1 count=0 seek=4294967296 status=none   # 4 GiB, sparse on disk
printf 'mid' | dd of=huge.bin bs=1 seek=2147483653 conv=notrunc status=none
printf 'end!' >> huge.bin
cd "$work"
flags="-noappend -no-xattrs -no-progress -quiet -mkfs-time 0 -all-time 0 -force-uid 0 -force-gid 0"
mksquashfs ext extended-inodes.sqsh -comp gzip -Xcompression-level 6 -b 128K $flags
mksquashfs huge huge-sparse.sqsh -comp gzip -Xcompression-level 6 -b 1M $flags
shasum -a 256 extended-inodes.sqsh huge-sparse.sqsh
cp extended-inodes.sqsh huge-sparse.sqsh "$(dirname "$0")/"
rm -rf "$work"
