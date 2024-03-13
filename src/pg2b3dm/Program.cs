using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using B3dm.Tileset;
using CommandLine;
using Npgsql;
using Newtonsoft.Json;
using subtree;
using B3dm.Tileset.Extensions;
using Wkx;
using SharpGLTF.Schema2;

namespace pg2b3dm;

class Program
{
    static string password = string.Empty;
    static bool skipCreateTiles = false; // could be useful for debugging purposes
    static void Main(string[] args)
    {
        var version = Assembly.GetEntryAssembly().GetName().Version;
        Console.WriteLine($"Tool: pg2b3dm {version}");
        Console.WriteLine("Options: " + string.Join(" ", args));
        Parser.Default.ParseArguments<Options>(args).WithParsed(o => {
            o.User = string.IsNullOrEmpty(o.User) ? Environment.UserName : o.User;
            o.Database = string.IsNullOrEmpty(o.Database) ? Environment.UserName : o.Database;

            var connectionString = $"Host={o.Host};Username={o.User};Database={o.Database};Port={o.Port};CommandTimeOut=0";
            var istrusted = TrustedConnectionChecker.HasTrustedConnection(connectionString);
            if (!istrusted) {
                Console.Write($"Password for user {o.User}: ");
                password = PasswordAsker.GetPassword();
                connectionString += $";password={password}";
                Console.WriteLine();
            }

            Console.WriteLine($"Start processing {DateTime.Now.ToLocalTime().ToString("s")}....");

            var stopWatch = new Stopwatch();
            stopWatch.Start();

            var output = o.Output;
            if (!Directory.Exists(output)) {
                Directory.CreateDirectory(output);
            }

            Console.WriteLine($"Input table: {o.GeometryTable}");
            if (o.Query != String.Empty) {
                Console.WriteLine($"Query:  {o.Query ?? "-"}");
            }
            Console.WriteLine($"Input geometry column: {o.GeometryColumn}");

            var table = o.GeometryTable;
            var geometryColumn = o.GeometryColumn;
            var defaultColor = o.DefaultColor;
            var defaultMetallicRoughness = o.DefaultMetallicRoughness;
            var doubleSided = (bool)o.DoubleSided;
            var createGltf = (bool)o.CreateGltf;

            var query = o.Query;

            var conn = new NpgsqlConnection(connectionString);

            var source_epsg = SpatialReferenceRepository.GetSpatialReference(conn, table, geometryColumn);

            if (source_epsg == 4978) {
                Console.WriteLine("----------------------------------------------------------------------------");
                Console.WriteLine("WARNING: Input geometries in ECEF (epsg:4978) are not supported in version >= 2.0.0");
                Console.WriteLine("Fix: Use local coordinate systems or EPSG:4326 in input datasource.");
                Console.WriteLine("----------------------------------------------------------------------------");
            }

            Console.WriteLine("App mode: " + o.AppMode);
            Console.WriteLine($"Spatial reference of {table}.{geometryColumn}: {source_epsg}");

            // Check spatialIndex
            var hasSpatialIndex = SpatialIndexChecker.HasSpatialIndex(conn, table, geometryColumn);
            if (!hasSpatialIndex) {
                Console.WriteLine();
                Console.WriteLine("-----------------------------------------------------------------------------");
                Console.WriteLine($"WARNING: No spatial index detected on {table}.{geometryColumn}");
                Console.WriteLine("Fix: add a spatial index, for example: ");
                Console.WriteLine($"'CREATE INDEX ON {table} USING gist(st_centroid(st_envelope({geometryColumn})))'");
                Console.WriteLine("-----------------------------------------------------------------------------");
                Console.WriteLine();
            }
            else {
                Console.WriteLine($"Spatial index detected on {table}.{geometryColumn}");
            }

            Console.WriteLine($"Query bounding box of {table}.{geometryColumn}...");
            var bbox_wgs84 = BoundingBoxRepository.GetBoundingBoxForTable(conn, table, geometryColumn);
            var bbox = bbox_wgs84.bbox;

            Console.WriteLine($"Bounding box for {table}.{geometryColumn} (in WGS84): " +
                $"{Math.Round(bbox.XMin, 8)}, {Math.Round(bbox.YMin, 8)}, " +
                $"{Math.Round(bbox.XMax, 8)}, {Math.Round(bbox.YMax, 8)}");

            var zmin = bbox_wgs84.zmin;
            var zmax = bbox_wgs84.zmax;

            Console.WriteLine($"Height values: [{Math.Round(zmin, 2)} m - {Math.Round(zmax, 2)} m]");
            Console.WriteLine($"Default color: {defaultColor}");
            Console.WriteLine($"Default metallic roughness: {defaultMetallicRoughness}");
            Console.WriteLine($"Doublesided: {doubleSided}");
            Console.WriteLine($"Create glTF tiles: {createGltf}");

            var att = !string.IsNullOrEmpty(o.AttributeColumns) ? o.AttributeColumns : "-";
            Console.WriteLine($"Attribute columns: {att}");

            var contentDirectory = $"{output}{Path.AltDirectorySeparatorChar}content";

            if (!Directory.Exists(contentDirectory)) {
                Directory.CreateDirectory(contentDirectory);
            }
            var center_wgs84 = bbox.GetCenter();
            Console.WriteLine($"Center (wgs84): {center_wgs84.X}, {center_wgs84.Y}");
            Tiles3DExtensions.RegisterExtensions();

            // cesium specific
            if (o.AppMode == AppMode.Cesium) {

                Console.WriteLine("Starting Cesium mode...");

                var translation = Translation.GetTranslation(center_wgs84);
                Console.WriteLine($"Translation ECEF: {String.Join(',', translation)}");

                var lodcolumn = o.LodColumn;
                var addOutlines = (bool)o.AddOutlines;
                var geometricErrors = Array.ConvertAll(o.GeometricErrors.Split(','), double.Parse);
                var useImplicitTiling = (bool)o.UseImplicitTiling;
                if (useImplicitTiling) {
                    if (!String.IsNullOrEmpty(lodcolumn)) {
                        Console.WriteLine("Warning: parameter -l --lodcolumn is ignored with implicit tiling");
                        lodcolumn = String.Empty;
                    }
                }
                // if useImpliciting is false and createGlb is false, the set use10 to true
                var use10 = !useImplicitTiling && !createGltf;
                Console.WriteLine("3D Tiles version: " + (use10 ? "1.0" : "1.1"));
                Console.WriteLine($"Lod column: {lodcolumn}");
                Console.WriteLine($"Radius column: {o.RadiusColumn}");
                Console.WriteLine($"Geometric errors: {String.Join(',', geometricErrors)}");
                Console.WriteLine($"Refinement: {o.Refinement}");

                var lods = (lodcolumn != string.Empty ? LodsRepository.GetLods(conn, table, lodcolumn, query) : new List<int> { 0 });
                if ((geometricErrors.Length != lods.Count + 1) && lodcolumn == string.Empty) {
                    Console.WriteLine($"Lod levels from database column {lodcolumn}: [{String.Join(',', lods)}]");
                    Console.WriteLine($"Geometric errors: {o.GeometricErrors}");

                    Console.WriteLine("Error: parameter -g --geometricerrors is wrongly specified...");
                    Console.WriteLine("end of program...");
                    Environment.Exit(0);
                }
                if (lodcolumn != String.Empty) {
                    Console.WriteLine($"Lod levels: {String.Join(',', lods)}");

                    if (lods.Count >= geometricErrors.Length) {
                        Console.WriteLine($"Calculating geometric errors starting from {geometricErrors[0]}");
                        geometricErrors = GeometricErrorCalculator.GetGeometricErrors(geometricErrors[0], lods);
                        Console.WriteLine($"Calculated geometric errors (for {lods.Count} levels): {String.Join(',', geometricErrors)}");
                    }
                };

                if (!useImplicitTiling) {
                    Console.WriteLine("Geometric errors used: " + String.Join(',', geometricErrors));
                }
                else {
                    Console.WriteLine("Geometric error used for implicit tiling: " + geometricErrors[0]);
                }
                Console.WriteLine($"Add outlines: {addOutlines}");
                Console.WriteLine($"Use 3D Tiles 1.1 implicit tiling: {o.UseImplicitTiling}");

                var rootBoundingVolumeRegion = bbox.ToRadians().ToRegion(zmin, zmax);

                var subtreesDirectory = $"{output}{Path.AltDirectorySeparatorChar}subtrees";

                Console.WriteLine($"Maximum features per tile: " + o.MaxFeaturesPerTile);

                var tile = new Tile(0, 0, 0);
                tile.BoundingBox = bbox.ToArray();
                Console.WriteLine($"Start generating tiles...");
                var quadtreeTiler = new QuadtreeTiler(conn, table, source_epsg, geometryColumn, o.MaxFeaturesPerTile, query, translation, o.ShadersColumn, o.AttributeColumns, lodcolumn, contentDirectory, lods, o.Copyright, skipCreateTiles, o.RadiusColumn);
                var tiles = quadtreeTiler.GenerateTiles(bbox, tile, new List<Tile>(), lodcolumn != string.Empty ? lods.First() : 0, addOutlines, defaultColor, defaultMetallicRoughness, doubleSided, createGltf);
                Console.WriteLine();
                Console.WriteLine("Tiles created: " + tiles.Count(tile => tile.Available));

                if (tiles.Count(tile => tile.Available) > 0) {
                    if (useImplicitTiling) {
                        if (!Directory.Exists(subtreesDirectory)) {
                            Directory.CreateDirectory(subtreesDirectory);
                        }

                        var subtreeFiles = SubtreeCreator.GenerateSubtreefiles(tiles);
                        Console.WriteLine($"Writing {subtreeFiles.Count} subtree files...");
                        foreach (var s in subtreeFiles) {
                            var t = s.Key;
                            var subtreefile = $"{subtreesDirectory}{Path.AltDirectorySeparatorChar}{t.Z}_{t.X}_{t.Y}.subtree";
                            File.WriteAllBytes(subtreefile, s.Value);
                        }

                        var subtreeLevels = subtreeFiles.Count > 1 ? ((Tile)subtreeFiles.ElementAt(1).Key).Z : 2;
                        var availableLevels = tiles.Max(t => t.Z) + 1;
                        Console.WriteLine("Available Levels: " + availableLevels);
                        Console.WriteLine("Subtree Levels: " + subtreeLevels);
                        var tilesetjson = TreeSerializer.ToImplicitTileset(translation, rootBoundingVolumeRegion, geometricErrors[0], availableLevels, subtreeLevels, version, createGltf);
                        var file = $"{o.Output}{Path.AltDirectorySeparatorChar}tileset.json";
                        var json = JsonConvert.SerializeObject(tilesetjson, Formatting.Indented, new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore });
                        Console.WriteLine("SubdivisionScheme: QUADTREE");
                        Console.WriteLine($"Writing {file}...");
                        File.WriteAllText(file, json);
                    }
                    else {
                        var splitLevel = (int)Math.Ceiling((tiles.Max((Tile s) => s.Z) + 1.0) / 2.0);

                        var rootTiles = TileSelector.Select(tiles, tile, 0, splitLevel);
                        var rootTileset = TreeSerializer.ToTileset(rootTiles, translation, rootBoundingVolumeRegion, geometricErrors, zmin, zmax, version, o.Refinement, use10);

                        var maxlevel = tiles.Max((Tile s) => s.Z);

                        if (maxlevel > splitLevel) {
                            // now create the tileset.json files on splitLevel

                            var width = Math.Pow(2, splitLevel);
                            var height = Math.Pow(2, splitLevel);
                            Console.WriteLine($"Writing tileset.json files...");

                            for (var i = 0; i < width; i++) {
                                for (var j = 0; j < height; j++) {
                                    var splitLevelTile = new Tile(splitLevel, i, j);
                                    var children = TileSelector.Select(tiles, splitLevelTile, splitLevel, maxlevel);
                                    if (children.Count > 0) {
                                        var childrenBoundingVolumeRegion = GetBoundingBox(children).ToRadians().ToRegion(zmin, zmax);

                                        /// translation is the same as identiy matrix in case of child tileset
                                        var tileset = TreeSerializer.ToTileset(children, null, childrenBoundingVolumeRegion, geometricErrors, zmin, zmax, version, o.Refinement, use10);
                                        var detailedJson = JsonConvert.SerializeObject(tileset, Formatting.Indented, new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore });
                                        var filename = $"tileset_{splitLevel}_{i}_{j}.json";
                                        Console.Write($"\rWriting {filename}...");

                                        File.WriteAllText($"{o.Output}{Path.AltDirectorySeparatorChar}{filename}", detailedJson);

                                        // add the child tilesets to the root tileset
                                        var child = new Child();
                                        child.boundingVolume = new Boundingvolume() { region = childrenBoundingVolumeRegion };
                                        child.refine = o.Refinement;
                                        child.geometricError = geometricErrors[0];
                                        child.content = new Content() { uri = filename };
                                        rootTileset.root.children.Add(child);
                                    }
                                }
                            }
                        }
                        // write the root tileset
                        Console.WriteLine();
                        Console.WriteLine("Writing root tileset.json...");
                        var rootJson = JsonConvert.SerializeObject(rootTileset, Formatting.Indented, new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore });
                        File.WriteAllText($"{o.Output}{Path.AltDirectorySeparatorChar}tileset.json", rootJson);
                    }
                }
                Console.WriteLine();

                // end cesium specific code

            }
            else {
                // mapbox specific code

                Console.WriteLine("Starting Experimental MapBox v3 mode...");

                var zoom = o.Zoom;

                var target_srs = 3857;
                var tiles = Tiles.Tools.Tilebelt.GetTilesOnLevel(new double[] { bbox.XMin, bbox.YMin, bbox.XMax, bbox.YMax }, zoom);

                Console.WriteLine($"Creating tiles for level {zoom}: {tiles.Count()}");

                foreach (var t in tiles) {
                    var bounds = t.Bounds();

                    var query1 = (query != string.Empty ? $" and {query}" : String.Empty);

                    var numberOfFeatures = FeatureCountRepository.CountFeaturesInBox(conn, table, geometryColumn, new Point(bounds[0], bounds[1]), new Point(bounds[2], bounds[3]), query1);

                    if (numberOfFeatures > 0) {
                        var ul = t.BoundsUL();
                        var ur = t.BoundsUR();
                        var ll = t.BoundsLL();

                        var ul_spherical = SphericalMercator.ToSphericalMercatorFromWgs84(ul.X, ul.Y);
                        var ur_spherical = SphericalMercator.ToSphericalMercatorFromWgs84(ur.X, ur.Y);
                        var ll_spherical = SphericalMercator.ToSphericalMercatorFromWgs84(ll.X, ll.Y);
                        var width = ur_spherical[0] - ul_spherical[0];
                        var height = ul_spherical[1] - ll_spherical[1];

                        var ext = createGltf ? "glb" : "b3dm";
                        var geometries = GeometryRepository.GetGeometrySubset(conn, table, geometryColumn, bounds, source_epsg, target_srs, o.ShadersColumn, o.AttributeColumns, query1);

                        // in Mapbox mode, every tile has 2^13 = 8192 values
                        // see https://github.com/mapbox/mapbox-gl-js/blob/main/src/style-spec/data/extent.js
                        var extent = 8192;
                        double[] scale = { extent / width, -1 * extent / height, 1 };
                        // in Mapbox mode
                        //  - we use YAxisUp = false
                        //  - all coordinates are relative to the upperleft coordinate
                        //  - Outlines is set to false because outlines extension is not supported (yet) in Mapbox client
                        var bytes = TileWriter.ToTile(geometries, new double[] { ul_spherical[0], ul_spherical[1], 0 }, scale, o.Copyright, false, defaultColor, defaultMetallicRoughness, createGltf: (bool)o.CreateGltf, YAxisUp: false);
                        File.WriteAllBytes($@"{contentDirectory}{Path.AltDirectorySeparatorChar}{t.Z}-{t.X}-{t.Y}.{ext}", bytes);
                        Console.Write(".");

                    }
                }
                Console.WriteLine();
                Console.WriteLine("Warning: Draco compress the resulting tiles. If not compressed, visualization in Mapbox will not be correct (v3.2.0)");
                // end mapbox specific code
            }

            stopWatch.Stop();

            var timeSpan = stopWatch.Elapsed;
            Console.WriteLine("Time: {0}h {1}m {2}s {3}ms", timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds, timeSpan.Milliseconds);
            Console.WriteLine($"Program finished {DateTime.Now.ToLocalTime().ToString("s")}.");
        });
    }

    private static BoundingBox GetBoundingBox(List<Tile> children)
    {
        var minx = children.Min(t => t.BoundingBox[0]);
        var maxx = children.Max(t => t.BoundingBox[2]);
        var miny = children.Min(t => t.BoundingBox[1]);
        var maxy = children.Max(t => t.BoundingBox[3]);
        return new BoundingBox(minx, miny, maxx, maxy);
    }
}
