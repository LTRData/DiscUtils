// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using ExFat;
using System;

namespace LibraryTests.ExFat.Tests;
public class DateTests
{
    [Fact]
    public void UInt16ToDateTime1()
    {
        var d = DateTimeUtility.FromTimeStamp(0b1011010_0111_00100__10001_101101_00011, 151);
        Assert.Equal(new DateTime(2070, 7, 4, 17, 45, 7, 510, DateTimeKind.Local), d);
    }

    [Fact]
    public void DateTimeToUInt161()
    {
        var dateTime = new DateTime(2070, 7, 4, 17, 45, 7, 510, DateTimeKind.Local);
        var (timeStamp, tenMs) = dateTime.ToTimeStamp();
        Assert.Equal(0b1011010_0111_00100__10001_101101_00011, timeStamp);
        Assert.Equal(151, tenMs);
    }

    [Fact]
    public void TimeZoneInfoUtc()
    {
        var t = DateTimeUtility.FromTimeZoneOffset(0x80);
        Assert.Equal(t, TimeSpan.FromHours(0));
    }

    [Fact]
    public void TimeZoneInfoDateLine()
    {
        var t = DateTimeUtility.FromTimeZoneOffset(0xD0);
        Assert.Equal(t, TimeSpan.FromHours(-12));
    }

    [Fact]
    public void TimeZoneInfoAzores()
    {
        var t = DateTimeUtility.FromTimeZoneOffset(0xFC);
        Assert.Equal(t, TimeSpan.FromHours(-1));
    }

    [Fact]
    [Trait("Category", "DateTimeOffset")]
    public void TimeZoneInfoCustom()
    {
        var t = DateTimeUtility.FromTimeZoneOffset(0xF3);
        Assert.Equal(t, TimeSpan.FromHours(-3.25));
    }

    [Fact]
    [Trait("Category", "DateTimeOffset")]
    public void NonLocalTimeOffsetFromLocal()
    {
        var t = new DateTime(2017, 11, 13, 12, 34, 56, DateTimeKind.Utc);
        var z = t.ToDateTimeOffset(TimeSpan.FromHours(8));
        Assert.Equal(8.0, z.Offset.TotalHours);
        Assert.Equal(20, z.Hour);
    }
}