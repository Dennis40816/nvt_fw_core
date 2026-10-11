// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Xunit;

namespace Nvt.Core.TestSupport.Tests;

/// <summary>Runs alone, so that no other test disturbs the measured calibration unit.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CalibrationIsolation
{
    /// <summary>The collection name.</summary>
    public const string Name = "Calibration";
}

/// <summary>Prints the calibration unit of this machine. <c>tools/repo-checks/duration_report.py</c> reads it from the TRX output.</summary>
/// <param name="output">Receives the unit line.</param>
[Collection(CalibrationIsolation.Name)]
public sealed class CalibrationTests(ITestOutputHelper output)
{
    /// <summary>Writes <c>NVT_CALIBRATION_UNIT_SECONDS=&lt;seconds&gt;</c> to the test output.</summary>
    [Fact]
    [Trait("Category", "Calibration")]
    public void PrintCalibrationUnit()
    {
        TimeSpan unit = RelativePerf.CalibrationUnit();
        Assert.True(unit > TimeSpan.Zero);
        output.WriteLine("NVT_CALIBRATION_UNIT_SECONDS=" + unit.TotalSeconds.ToString("R", CultureInfo.InvariantCulture));
    }
}
