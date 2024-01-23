﻿using NUnit.Framework;
using Wkx;

namespace Wkb2Gltf.Tests;

public class GeometryRecordTests
{
    [Test]
    public void GeometryRecordFirstTest()
    {
        // arrange
        var geometryRecord = new GeometryRecord(0);
        var wkt = "MULTIPOLYGON Z (((133836.921875 463013.96875 -2.280999898910522,133842.046875 463004.65625 -2.280999898910522,133833.359375 462999.84375 -2.280999898910522,133831.21875 462998.65625 -2.280999898910522,133830.484375 463000 -2.280999898910522,133826.078125 463008.03125 -2.280999898910522,133828.03125 463009.09375 -2.280999898910522,133836.921875 463013.96875 -2.280999898910522)),((133833.359375 462999.84375 2.655999898910522,133833.359375 462999.84375 -2.280999898910522,133842.046875 463004.65625 -2.280999898910522,133842.046875 463004.65625 -0.250999987125397,133833.359375 462999.84375 2.655999898910522)),((133831.21875 462998.65625 0.360000014305115,133831.21875 462998.65625 -2.280999898910522,133833.359375 462999.84375 -2.280999898910522,133833.359375 462999.84375 2.655999898910522,133833.359375 462999.84375 2.776999950408936,133831.21875 462998.65625 0.360000014305115)),((133828.03125 463009.09375 2.638000011444092,133828.03125 463009.09375 2.555000066757202,133830.1875 463005.34375 2.644999980926514,133828.03125 463009.09375 2.638000011444092)),((133833.359375 462999.84375 2.776999950408936,133833.359375 462999.84375 2.655999898910522,133830.1875 463005.34375 2.644999980926514,133833.359375 462999.84375 2.776999950408936)),((133836.921875 463013.96875 -0.331999987363815,133836.921875 463013.96875 -2.280999898910522,133828.03125 463009.09375 -2.280999898910522,133828.03125 463009.09375 2.555000066757202,133828.03125 463009.09375 2.638000011444092,133836.921875 463013.96875 -0.331999987363815)),((133830.484375 463000 0.358999997377396,133830.484375 463000 -2.280999898910522,133831.21875 462998.65625 -2.280999898910522,133831.21875 462998.65625 0.360000014305115,133830.484375 463000 0.358999997377396)),((133842.046875 463004.65625 -0.250999987125397,133842.046875 463004.65625 -2.280999898910522,133836.921875 463013.96875 -2.280999898910522,133836.921875 463013.96875 -0.331999987363815,133842.046875 463004.65625 -0.250999987125397)),((133826.078125 463008.03125 0.354000002145767,133826.078125 463008.03125 -2.280999898910522,133830.484375 463000 -2.280999898910522,133830.484375 463000 0.358999997377396,133826.078125 463008.03125 0.354000002145767)),((133828.03125 463009.09375 2.555000066757202,133828.03125 463009.09375 -2.280999898910522,133826.078125 463008.03125 -2.280999898910522,133826.078125 463008.03125 0.354000002145767,133828.03125 463009.09375 2.555000066757202)),((133842.046875 463004.65625 -0.250999987125397,133836.921875 463013.96875 -0.331999987363815,133828.03125 463009.09375 2.638000011444092,133830.1875 463005.34375 2.644999980926514,133833.359375 462999.84375 2.655999898910522,133842.046875 463004.65625 -0.250999987125397)),((133828.03125 463009.09375 2.555000066757202,133826.078125 463008.03125 0.354000002145767,133830.484375 463000 0.358999997377396,133831.21875 462998.65625 0.360000014305115,133833.359375 462999.84375 2.776999950408936,133830.1875 463005.34375 2.644999980926514,133828.03125 463009.09375 2.555000066757202)))";
        var g = Geometry.Deserialize<WktSerializer>(wkt);
        var multipolygon = ((MultiPolygon)g);

        geometryRecord.Geometry = multipolygon;

        // act
        var triangles = geometryRecord.GetTriangles(new double[] {0,0,0});

        // assert
        Assert.That(g != null, Is.True);
        Assert.That(multipolygon.Geometries.Count == 12, Is.True);
        Assert.That(triangles.Count == 30, Is.True);
    }
}
