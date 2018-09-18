﻿// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using KdSoft.Lmdb;
using NUnit.Framework;
using Spreads.Utils;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Spreads.LMDB.Tests
{
    [TestFixture]
    public class PerfTests
    {
        [Test]
        public unsafe void SimpleWriteReadBenchmark()
        {
            var count = 1_000_000;
            var rounds = 10;

            var path = "./data/benchmark";
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }

            var dirS = Path.Combine(path, "Spreads");
            var dirK = Path.Combine(path, "KdSoft");
            Directory.CreateDirectory(dirS);
            Directory.CreateDirectory(dirK);

            var envS = LMDBEnvironment.Create(dirS, DbEnvironmentFlags.NoSync,
                disableAsync: true);
            envS.MaxDatabases = 10;
            envS.MapSize = 256 * 1024 * 1024;
            envS.Open();
            var dbS = envS.OpenDatabase("SimpleWrite", new DatabaseConfig(DbFlags.Create | DbFlags.IntegerKey));

            var envConfig = new EnvironmentConfiguration(10, mapSize: 256 * 1024 * 1024);
            var envK = new KdSoft.Lmdb.Environment(envConfig);
            envK.Open(dirK, EnvironmentOptions.NoSync);

            var config = new DatabaseConfiguration(DatabaseOptions.Create | DatabaseOptions.IntegerKey);
            KdSoft.Lmdb.Database dbase;
            using (var tx = envK.BeginDatabaseTransaction(TransactionModes.None))
            {
                dbase = tx.OpenDatabase("SimpleWrite", config);
                tx.Commit();
            }

            for (int r = 0; r < rounds; r++)
            {
                using (Benchmark.Run("Spreads Write", count, true))
                {
                    for (long i = r * count; i < (r + 1) * count; i++)
                    {
                        using (var tx = envS.BeginTransaction())
                        {
                            dbS.Put(tx, i, i, TransactionPutOptions.AppendData);
                            tx.Commit();
                        }
                    }
                }

                using (Benchmark.Run("Spreads Read", count, true))
                {
                    for (long i = r * count; i < (r + 1) * count; i++)
                    {
                        using (var tx = envS.BeginReadOnlyTransaction())
                        {
                            dbS.TryGet(tx, ref i, out long val);
                            if (val != i)
                            {
                                Assert.Fail();
                            }
                        }
                    }
                }

                using (Benchmark.Run("KdSoft Write", count, true))
                {
                    for (long i = r * count; i < (r + 1) * count; i++)
                    {
                        var ptr = Unsafe.AsPointer(ref i);
                        var span = new Span<byte>(ptr, 8);

                        using (var tx = envK.BeginTransaction(TransactionModes.None))
                        {
                            dbase.Put(tx, span, span, PutOptions.AppendData);
                            tx.Commit();
                        }
                    }
                }

                using (Benchmark.Run("KdSoft Read", count, true))
                {
                    for (long i = r * count; i < (r + 1) * count; i++)
                    {
                        var ptr = Unsafe.AsPointer(ref i);
                        var span = new Span<byte>(ptr, 8);

                        using (var tx = envK.BeginReadOnlyTransaction())
                        {
                            dbase.Get(tx, span, out var data);
                            var val = MemoryMarshal.Cast<byte, long>(data)[0];
                            if (val != i)
                            {
                                Assert.Fail();
                            }
                        }
                    }
                }
            }

            envS.Close();
            envK.Close();
            Benchmark.Dump("SimpleWrite/Read 10x1M longs");
        }

        [Test]
        public unsafe void SimpleWriteReadBatchedBenchmark()
        {
#pragma warning disable 618
            Settings.DoAdditionalCorrectnessChecks = false;
#pragma warning restore 618
            var count = 1_000_000;
            var rounds = 10;

            var path = "./data/benchmarkbatched";
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }

            var dirS = Path.Combine(path, "Spreads");
            var dirK = Path.Combine(path, "KdSoft");
            Directory.CreateDirectory(dirS);
            Directory.CreateDirectory(dirK);

            var envS = LMDBEnvironment.Create(dirS, DbEnvironmentFlags.NoSync,
                disableAsync: true);
            envS.MaxDatabases = 10;
            envS.MapSize = 256 * 1024 * 1024;
            envS.Open();
            var dbS = envS.OpenDatabase("SimpleWrite", new DatabaseConfig(DbFlags.Create | DbFlags.IntegerKey));

            var envConfig = new EnvironmentConfiguration(10, mapSize: 256 * 1024 * 1024);
            var envK = new KdSoft.Lmdb.Environment(envConfig);
            envK.Open(dirK, EnvironmentOptions.NoSync);

            var config = new DatabaseConfiguration(DatabaseOptions.Create | DatabaseOptions.IntegerKey);
            KdSoft.Lmdb.Database dbase;
            using (var tx = envK.BeginDatabaseTransaction(TransactionModes.None))
            {
                dbase = tx.OpenDatabase("SimpleWrite", config);
                tx.Commit();
            }

            // var garbage1 = new byte[1];
            
            for (int r = 0; r < rounds; r++)
            {
                using (Benchmark.Run("Spreads Write", count, true))
                {
                    using (var tx = envS.BeginTransaction())
                    {
                        for (long i = r * count; i < (r + 1) * count; i++)
                        {
                            dbS.Put(tx, i, i, TransactionPutOptions.AppendData);
                        }
                        tx.Commit();
                    }
                }

                // var extraRounds = 100000_000_000 / count;

                //var task = Task.Run(() =>
                //{
                //    var rng = new System.Random();
                //    var sw = new SpinWait();
                //    while (true)
                //    {
                //        try
                //        {
                //            var garbage = new byte[rng.Next(50000, 80000)];
                //            garbage[(int)((garbage.Length - 1) * rng.NextDouble())] = 42;
                //            if (garbage[(int)((garbage.Length - 1) * rng.NextDouble())] != 42)
                //            {
                //                // sw.SpinOnce();
                //                garbage[0] = (byte)(garbage[(int)((garbage.Length - 1) * rng.NextDouble())] + 1);
                //                // if (rng.NextDouble() > 0.95)
                //                {
                //                    garbage1 = garbage;
                //                    GC.Collect();
                //                }
                //            }
                //        }
                //        catch (Exception ex)
                //        {
                //            Console.WriteLine(ex);
                //            GC.Collect();
                //        }
                //    }

                //});

                using (Benchmark.Run("Spreads Read", count, true))
                {
                    using (var tx = envS.BeginReadOnlyTransaction())
                    {
                        
                        // for (long j = 0; j < extraRounds; j++)
                        {
                            for (long i = r * count; i < (r + 1) * count; i++)
                            {
                                dbS.TryGet(tx, ref i, out long val);
                                if (val != i)
                                {
                                    Assert.Fail();
                                }
                            }
                            GC.Collect();
                        }
                    }
                }

                //using (Benchmark.Run("KdSoft Write", count, true))
                //{
                //    using (var tx = envK.BeginTransaction(TransactionModes.None))
                //    {
                //        for (long i = r * count; i < (r + 1) * count; i++)
                //        {
                //            var ptr = Unsafe.AsPointer(ref i);
                //            var span = new Span<byte>(ptr, 8);

                //            dbase.Put(tx, span, span, PutOptions.AppendData);
                //        }
                //        tx.Commit();
                //    }
                //}

                //using (Benchmark.Run("KdSoft Read", count, true))
                //{
                //    using (var tx = envK.BeginReadOnlyTransaction())
                //    {
                //        for (long i = r * count; i < (r + 1) * count; i++)
                //        {
                //            var ptr = Unsafe.AsPointer(ref i);
                //            var span = new Span<byte>(ptr, 8);

                //            dbase.Get(tx, span, out var data);
                //            var val = MemoryMarshal.Cast<byte, long>(data)[0];
                //            if (val != i)
                //            {
                //                Assert.Fail();
                //            }
                //        }
                //    }
                //}
            }

            envS.Close();
            envK.Close();
            Benchmark.Dump("SimpleBatchedWrite/Read 10x1M longs");
            // Console.WriteLine(garbage1[0]);
        }

        [Test]
        public void DiskSyncWriteRead()
        {
            var count = 5_000;
            var rounds = 1;

            var path = "./data/benchmark";
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }

            var dirS = Path.Combine(path, "Spreads");
            var dirK = Path.Combine(path, "KdSoft");
            Directory.CreateDirectory(dirS);
            Directory.CreateDirectory(dirK);

            var envS = LMDBEnvironment.Create(dirS, DbEnvironmentFlags.WriteMap | DbEnvironmentFlags.NoMetaSync,
                disableAsync: true);
            envS.MaxDatabases = 10;
            envS.MapSize = 16 * 1024 * 1024;
            envS.Open();
            var dbS = envS.OpenDatabase("SimpleWrite", new DatabaseConfig(DbFlags.Create | DbFlags.IntegerKey));

            for (int r = 0; r < rounds; r++)
            {
                using (Benchmark.Run("Spreads Write", count * 1_000_000, true))
                {
                    for (long i = r * count; i < (r + 1) * count; i++)
                    {
                        using (var tx = envS.BeginTransaction())
                        {
                            dbS.Put(tx, i, i, TransactionPutOptions.AppendData);
                            tx.Commit();
                        }
                    }
                }

                using (Benchmark.Run("Spreads Read", count, true))
                {
                    for (long i = r * count; i < (r + 1) * count; i++)
                    {
                        using (var tx = envS.BeginReadOnlyTransaction())
                        {
                            dbS.TryGet(tx, ref i, out long val);
                            if (val != i)
                            {
                                Assert.Fail();
                            }
                        }
                    }
                }
            }

            envS.Close();
            Benchmark.Dump("Writes in single OPS");
        }
    }
}