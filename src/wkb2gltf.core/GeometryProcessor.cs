﻿using System;
using System.Collections.Generic;
using System.Linq;
using Triangulate;
using Wkx;

namespace Wkb2Gltf;

public static class GeometryProcessor
{
    private static Geometry GetGeometry(Geometry multiGeometry, int i)
    {
        switch (multiGeometry) {
            case MultiPolygon multiPolygon:
                return multiPolygon.Geometries[i];
            case MultiLineString multiLineString:
                return multiLineString.Geometries[i];
            case PolyhedralSurface polyhedralSurface:
                return polyhedralSurface.Geometries[i];
            default:
                throw new NotSupportedException($"Geometry type {multiGeometry.GeometryType} is not supported");
        }
    }

    private static int Count (Geometry multiGeometry)
    {
       switch (multiGeometry)
        {
            case MultiPolygon multiPolygon:
                return multiPolygon.Geometries.Count;
            case MultiLineString multiLineString:
                return multiLineString.Geometries.Count;
            case PolyhedralSurface polyhedralSurface:
                return polyhedralSurface.Geometries.Count;
            default:
                throw new NotSupportedException($"Geometry type {multiGeometry.GeometryType} is not supported");
        }
    }

    public static List<Triangle> GetTriangles(Geometry geometry, int batchId, double[] translation = null, double[] scale = null, ShaderColors shadercolors = null, float? radius = null)
    {
        var r = radius.HasValue ? radius.Value : (float)1.0f;

        if (geometry is MultiPolygon || geometry is MultiLineString || geometry is PolyhedralSurface)
        {
            if(shadercolors != null)
            {
                var numberOfGeometries = Count(geometry);

                if (numberOfGeometries == shadercolors.Count()) {
                    return GetTrianglesForMultiGeometries(geometry, batchId, translation, scale, shadercolors, r, numberOfGeometries);
                }
            }
        }

        List<Polygon> geometries;

        switch (geometry)
        {
            case LineString lineString:
                geometries = GetTrianglesFromLines(lineString, r, translation, scale);
                break;
            case MultiLineString multiLineString:
                geometries = GetTrianglesFromLines(multiLineString, r, translation, scale);
                break;
            case Polygon:
            case MultiPolygon:
            case PolyhedralSurface:
                geometries = GetTrianglesFromPolygons(geometry, translation);
                break;
            case Tin tin:
                var nmp = new PolyhedralSurface(tin.Geometries.Select(t => new Polygon(t.ExteriorRing.Points)));
                geometries = GetTrianglesFromPolygons(nmp, translation);
                break;
            default:
                throw new NotSupportedException($"Geometry type {geometry.GeometryType} is not supported");
        }

        var result = GetTriangles(batchId, shadercolors, geometries);

        return result;
    }

    private static List<Triangle> GetTrianglesForMultiGeometries(Geometry geometry, int batchId, double[] translation, double[] scale, ShaderColors shadercolors, float? radius, int numberOfGeometries)
    {
        var result1 = new List<Triangle>();
        // Do special treatment
        for (var i = 0; i < numberOfGeometries; i++) {
            var geom = GetGeometry(geometry, i);
            var shader = shadercolors.ToShader(i);
            var shaderColor = ShaderColors.ToShaderColors(shader);

            var geometryRecord = new GeometryRecord(batchId) {
                Geometry = geom,
                Shader = shaderColor,
                Radius = radius
            };
            result1.AddRange(geometryRecord.GetTriangles(translation, scale));
        }
        return result1;
    }


    private static List<Polygon> GetTrianglesFromLines(MultiLineString line, float radius, double[] translation = null, double[] scale = null, int? tabularSegments = 64, int? radialSegments = 8)
    {
        var relativeLine = GetRelativeLine(line, translation, scale);
        var triangles = Triangulator.Triangulate(relativeLine, radius, radialSegments: radialSegments);
        return triangles.Geometries;
    }

    private static List<Polygon> GetTrianglesFromLines(LineString line, float radius, double[] translation = null, double[] scale = null, int? tabularSegments = 64, int? radialSegments = 8)
    {
        var relativeLine = GetRelativeLine(line, translation, scale);
        var triangles = Triangulator.Triangulate(relativeLine, radius, radialSegments: radialSegments);
        return triangles.Geometries;
    }


    private static List<Polygon> GetTrianglesFromPolygons(Geometry geometry, double[] translation = null, double[] scale = null)
    {
        var geometries = GetGeometries(geometry);

        var isTriangulated = IsTriangulated(geometries);

        var relativePolygons = GetRelativePolygons(geometries, translation, scale);
        var m1 = new MultiPolygon(relativePolygons);

        if (!isTriangulated) {
            var triangles = Triangulator.Triangulate(m1);
            geometries = ((MultiPolygon)triangles).Geometries;
        }
        else {
            geometries = m1.Geometries;
        }

        return geometries;
    }

    private static List<Polygon> GetGeometries(Geometry geometry)
    {
        switch (geometry)
        {
            case Polygon polygon:
                return new List<Polygon>() { polygon };
            case MultiPolygon multiPolygon:
                return multiPolygon.Geometries;
            case PolyhedralSurface surface:
                return surface.Geometries;
            default:
                throw new NotSupportedException($"Geometry type {geometry.GeometryType} is not supported");
        }
    }

    private static MultiLineString GetRelativeLine(MultiLineString multiline, double[] translation = null, double[] scale = null)
    {
        var result = new MultiLineString();
        foreach (var line in multiline.Geometries) {
            var relativeLine = GetRelativeLine(line, translation, scale);
            result.Geometries.Add(relativeLine);
        }
        return result;
    }

    private static LineString GetRelativeLine(LineString line, double[] translation = null, double[] scale = null)
    {
        var result = new LineString();
        foreach (var pnt in line.Points) {
            var relativePoint = ToRelativePoint(pnt, translation, scale);
            result.Points.Add(relativePoint);
        }
        return result;
    }

    private static List<Polygon> GetRelativePolygons(List<Polygon> geometries, double[] translation = null, double[] scale = null)
    {
        var relativePolygons = new List<Polygon>();

        foreach (var geometry in geometries) {
            var exteriorRing = new LinearRing();
            var interiorRings = new List<LinearRing>();
            foreach (var point in geometry.ExteriorRing.Points) {
                var relativePoint = ToRelativePoint(point, translation, scale);
                exteriorRing.Points.Add(relativePoint);
            }

            foreach (var interiorRing in geometry.InteriorRings) {
                var relativeInteriorRing = new LinearRing();
                foreach (var point in interiorRing.Points) {
                    var relativePoint = ToRelativePoint(point, translation, scale);
                    relativeInteriorRing.Points.Add(relativePoint);
                }
                interiorRings.Add(relativeInteriorRing);
            }
            var relativePolygon = new Polygon(exteriorRing, interiorRings);
            relativePolygons.Add(relativePolygon);
        }

        return relativePolygons;
    }

    private static List<Triangle> GetTriangles(int batchId, ShaderColors shadercolors, List<Polygon> geometries)
    {
        var degenerated_triangles = 0;
        var allTriangles = new List<Triangle>();
        for (var i = 0; i < geometries.Count; i++) {
            var geometry = geometries[i];
            var triangle = GetTriangle(geometry, batchId);

            if (triangle != null && shadercolors != null) {
                shadercolors.Validate(geometries.Count);
                triangle.Shader = shadercolors.ToShader(i);
            }

            if (triangle != null) {
                allTriangles.Add(triangle);
            }
            else {
                degenerated_triangles++;
            }
        }

        return allTriangles;
    }

    public static Triangle GetTriangle(Polygon geometry, int batchId)
    {
        var pnts = geometry.ExteriorRing.Points;
        if (pnts.Count != 4) {
            throw new ArgumentOutOfRangeException($"Expected number of vertices in triangles: 4, actual: {pnts.Count}");
        }

        var triangle = new Triangle(pnts[0], pnts[1], pnts[2], batchId);

        return triangle;
    }

    private static Point ToRelativePoint(Point pnt, double[] translation = null, double[] scale = null)
    {
        Point res;
        if (translation != null) {
            res = new Point((double)pnt.X - translation[0], (double)pnt.Y - translation[1], pnt.Z - translation[2]);
        }
        else {
            res = pnt;
        }
        if (scale != null) {
            res = new Point((double)res.X * scale[0], (double)res.Y * scale[1], res.Z * scale[2]);
        }

        return res;
    }

    private static bool IsTriangulated(List<Polygon> polygons)
    {
        foreach (var polygon in polygons) {
            if (polygon.ExteriorRing.Points.Count != 4) {
                return false;
            }
        }
        return true;
    }
}
