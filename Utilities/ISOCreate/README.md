# ISOCreate

Create an optical disc image from a source directory:

```text
ISOCreate [options] image_file sourcedir [bootimage]
```

The default is ISO 9660 with Joliet, using the existing ISO builder. Add `-udf`
to create a UDF 2.01 image. The output filename extension does not select the
format.

```text
ISOCreate -vl MY_DISC output.iso source
ISOCreate -udf -vl MY_DISC output.iso source
```

`-vl label` (or `-vollabel label`) sets the volume label for either format.
UDF labels use the builder's 31-byte encoded label limit (including the CS0
compression identifier); the default UDF label is `DISC`.

UDF mode includes files, subdirectories and empty directories. It writes the
complete image and file contents to the output file.

The optional `bootimage` parameter creates an El Torito bootable ISO, as before.
Combining it with `-udf` reports an error and exits with code 1 before writing the
output image. UDF mode does not create a bootable or hybrid ISO/UDF image.

Use `ISOCreate -help` to see all options.
