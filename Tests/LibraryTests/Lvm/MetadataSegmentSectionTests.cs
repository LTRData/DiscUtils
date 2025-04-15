using System;
using System.Collections.Generic;
using System.IO;
using DiscUtils.Lvm;
using Xunit;

namespace LibraryTests.Lvm;

public class MetadataSegmentSectionTests
{
    public static IEnumerable<object[]> MetadataTestCases =>
    [
        [
            "segment1 {",
            """
            start_extent = 0
            extent_count = 2560
            type = "striped"
            stripe_count = 1
            stripes = ["pv0", 3840]
            }
            """,
            new MetadataSegmentSection
            {
                Name = "segment1",
                StartExtent = 0,
                ExtentCount = 2560,
                Type = SegmentType.Striped,
                StripeCount = 1,
                Stripes =
                [
                    new MetadataStripe
                    {
                        PhysicalVolumeName = "pv0",
                        StartExtentNumber = 3840,
                    },
                ],
            },
        ],
        [
            "segment2 {",
            """
            start_extent = 0
            extent_count = 94
            type = "striped"
            stripe_count = 1
            stripes = [
            	"pv0", 1813
            ]
            }
            """,
            new MetadataSegmentSection
            {
                Name = "segment2",
                StartExtent = 0,
                ExtentCount = 94,
                Type = SegmentType.Striped,
                StripeCount = 1,
                Stripes =
                [
                    new MetadataStripe
                    {
                        PhysicalVolumeName = "pv0",
                        StartExtentNumber = 1813,
                    },
                ],
            },
        ],
        [
            "segment3 {",
            """
            start_extent = 0
            extent_count = 768
            type = "striped"
            stripe_count = 2
            stripes = ["pv0", 512, "pv1", 0]
            }
            """,
            new MetadataSegmentSection
            {
                Name = "segment3",
                StartExtent = 0,
                ExtentCount = 768,
                StripeCount = 2,
                Type = SegmentType.Striped,
                Stripes =
                [
                    new MetadataStripe
                    {
                        PhysicalVolumeName = "pv0",
                        StartExtentNumber = 512,
                    },
                    new MetadataStripe
                    {
                        PhysicalVolumeName = "pv1",
                        StartExtentNumber = 0,
                    },
                ],
            },
        ],
        [
            "segment4 {",
            """
            start_extent = 0
            extent_count = 1024
            type = "striped"
            stripe_count = 3
            stripes = [
                "pv0", 1280, "pv1", 768,
                "pv2", 0
            ]
            }
            """,
            new MetadataSegmentSection
            {
                Name = "segment4",
                StartExtent = 0,
                ExtentCount = 1024,
                Type = SegmentType.Striped,
                StripeCount = 3,
                Stripes =
                [
                    new MetadataStripe
                    {
                        PhysicalVolumeName = "pv0",
                        StartExtentNumber = 1280,
                    },
                    new MetadataStripe
                    {
                        PhysicalVolumeName = "pv1",
                        StartExtentNumber = 768,
                    },
                    new MetadataStripe
                    {
                        PhysicalVolumeName = "pv2",
                        StartExtentNumber = 0,
                    },
                ],
            },
        ],
    ];

    [Theory()]
    [MemberData(nameof(MetadataTestCases))]
    internal void ParseShouldSucceedForStripedSegment(string head, string dataString, MetadataSegmentSection expectedMetadataSegmentSection)
    {
        var metadataSegmentSection = new MetadataSegmentSection();
        metadataSegmentSection.Parse(head, new StringReader(dataString));

        Assert.Equivalent(expectedMetadataSegmentSection, metadataSegmentSection);
    }

    [Fact]
    public void ParseShouldThrowForInvalidStripedSegment()
    {
        var textReader = new StringReader(
            """
            start_extent = 0
            extent_count = 2560
            type = "striped"
            stripe_count = 1
            stripes = ["pv0", 3840
            ]
            }
            """);

        var metadataSegmentSection = new MetadataSegmentSection();

        var actualException = Assert.Throws<ArgumentException>(() => metadataSegmentSection.Parse("segment1 {", textReader));
        Assert.StartsWith("Unsupported or invalid stripe format", actualException.Message);
    }
}
