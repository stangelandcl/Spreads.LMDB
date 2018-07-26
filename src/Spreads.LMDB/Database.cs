﻿// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Spreads.Buffers;
using Spreads.Collections.Concurrent;
using Spreads.LMDB.Interop;
using Spreads.Serialization;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using static System.Runtime.CompilerServices.Unsafe;

namespace Spreads.LMDB
{
    /// <summary>
    /// Database.
    /// </summary>
    public class Database : IDisposable
    {
        internal readonly ObjectPool<ReadCursorHandle> ReadHandlePool =
            new ObjectPool<ReadCursorHandle>(() => new ReadCursorHandle(), System.Environment.ProcessorCount * 16);

        internal uint _handle;
        private readonly DatabaseConfig _config;
        private readonly LMDBEnvironment _lmdbEnvironment;
        private readonly string _name;

        internal Database(string name, TransactionImpl txn, DatabaseConfig config)
        {
            if (txn.IsReadOnly) { throw new InvalidOperationException("Cannot create a DB with RO transaction"); }
            if (txn == null) { throw new ArgumentNullException(nameof(txn)); }
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _name = name;
            _lmdbEnvironment = txn.LmdbEnvironment;

            NativeMethods.AssertExecute(NativeMethods.mdb_dbi_open(txn._writeHandle, name, _config.OpenFlags, out var handle));
            if (_config.CompareFunction != null)
            {
                NativeMethods.AssertExecute(NativeMethods.mdb_set_compare(txn._writeHandle, handle, _config.CompareFunction));
            }
            if (_config.DupSortFunction != null)
            {
                NativeMethods.AssertExecute(NativeMethods.mdb_set_dupsort(txn._writeHandle, handle, _config.DupSortFunction));
            }
            if (_config.DupSortPrefix > 0)
            {
                if (_config.DupSortPrefix == 96)
                {
                    NativeMethods.AssertExecute(NativeMethods.sdb_set_dupsort_as_uint96(txn._writeHandle, handle));
                }
                else if (_config.DupSortPrefix == 80)
                {
                    NativeMethods.AssertExecute(NativeMethods.sdb_set_dupsort_as_uint80(txn._writeHandle, handle));
                }
                else if (_config.DupSortPrefix == 64)
                {
                    NativeMethods.AssertExecute(NativeMethods.sdb_set_dupsort_as_uint64(txn._writeHandle, handle));
                }
                else if (_config.DupSortPrefix == 48)
                {
                    NativeMethods.AssertExecute(NativeMethods.sdb_set_dupsort_as_uint48(txn._writeHandle, handle));
                }
                else if (_config.DupSortPrefix == 32)
                {
                    NativeMethods.AssertExecute(NativeMethods.sdb_set_dupsort_as_uint32(txn._writeHandle, handle));
                }
                else if (_config.DupSortPrefix == 16)
                {
                    NativeMethods.AssertExecute(NativeMethods.sdb_set_dupsort_as_uint16(txn._writeHandle, handle));
                }
                else
                {
                    throw new NotSupportedException("Rethink your design if you need this!");
                }
            }

            _handle = handle;
        }

        internal bool IsReleased => _handle == default(uint);

        /// <summary>
        /// Is database opened.
        /// </summary>
        public bool IsOpen => _handle != default;

        /// <summary>
        /// Database name.
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// Environment in which the database was opened.
        /// </summary>
        public LMDBEnvironment LmdbEnvironment => _lmdbEnvironment;

        /// <summary>
        /// Flags with which the database was opened.
        /// </summary>
        public DbFlags OpenFlags => _config.OpenFlags;

        /// <summary>
        /// Open Read/Write cursor.
        /// </summary>
        public Cursor OpenCursor(Transaction txn)
        {
            return new Cursor(CursorImpl.Create(this, txn._impl, null));
        }

        public ReadOnlyCursor OpenReadOnlyCursor(ReadOnlyTransaction txn)
        {
            var rh = ReadHandlePool.Allocate();
            return new ReadOnlyCursor(CursorImpl.Create(this, txn._impl, rh));
        }

        /// <summary>
        /// Drops the database.
        /// </summary>
        public Task Drop()
        {
            return LmdbEnvironment.WriteAsync(txn =>
            {
                NativeMethods.AssertExecute(NativeMethods.mdb_drop(txn._impl._writeHandle, _handle, true));
                txn.Commit();
                _handle = default;
                return null;
            });
        }

        /// <summary>
        /// Drops the database inside the given transaction.
        /// </summary>
        public bool Drop(Transaction transaction)
        {
            var res = NativeMethods.AssertExecute(NativeMethods.mdb_drop(transaction._impl._writeHandle, _handle, true));
            _handle = default;
            return res == 0;
        }

        /// <summary>
        /// Truncates all data from the database.
        /// </summary>
        public Task Truncate()
        {
            return LmdbEnvironment.WriteAsync(txn =>
            {
                NativeMethods.AssertExecute(NativeMethods.mdb_drop(txn._impl._writeHandle, _handle, false));
                txn.Commit();
                return null;
            });
        }

        /// <summary>
        /// Truncates all data from the database inside the given transaction.
        /// </summary>
        public bool Truncate(Transaction transaction)
        {
            var res = NativeMethods.AssertExecute(NativeMethods.mdb_drop(transaction._impl._writeHandle, _handle, false));
            return res == 0;
        }

        public MDB_stat GetStat()
        {
            using (var tx = TransactionImpl.Create(LmdbEnvironment, TransactionBeginFlags.ReadOnly))
            {
                NativeMethods.AssertRead(NativeMethods.mdb_stat(tx._readHandle.Handle, _handle, out var stat));
                return stat;
            }
        }

        public long GetEntriesCount()
        {
            var stat = GetStat();
            return stat.ms_entries.ToInt64();
        }

        public long GetUsedSize()
        {
            var stat = GetStat();
            var totalPages =
                stat.ms_branch_pages.ToInt64() +
                stat.ms_leaf_pages.ToInt64() +
                stat.ms_overflow_pages.ToInt64();
            return stat.ms_psize * totalPages;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Put(Transaction txn, ref DirectBuffer key, ref DirectBuffer value,
            TransactionPutOptions flags = TransactionPutOptions.None)
        {
            NativeMethods.AssertExecute(NativeMethods.mdb_put(txn._impl._writeHandle, _handle,
                ref key, ref value, flags));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Put<TKey, TValue>(Transaction txn, ReadOnlyMemory<TKey> key, ReadOnlyMemory<TValue> value,
            TransactionPutOptions flags = TransactionPutOptions.None)
            where TKey : struct where TValue : struct
        {
            var keyBytesSpan = MemoryMarshal.Cast<TKey, byte>(key.Span);
            var valueBytesSpan = MemoryMarshal.Cast<TValue, byte>(value.Span);
            fixed (byte* keyPtr = &MemoryMarshal.GetReference(keyBytesSpan), valuePtr = &MemoryMarshal.GetReference(valueBytesSpan))
            {
                var key1 = new DirectBuffer(keyBytesSpan.Length, keyPtr);
                var value1 = new DirectBuffer(valueBytesSpan.Length, valuePtr);
                NativeMethods.AssertExecute(NativeMethods.mdb_put(txn._impl._writeHandle, _handle,
                    ref key1, ref value1,
                    flags));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Put<TKey, TValue>(Transaction txn, TKey key, TValue value,
            TransactionPutOptions flags = TransactionPutOptions.None)
            where TKey : struct where TValue : struct
        {
            var keyPtr = AsPointer(ref key);
            var valuePtr = AsPointer(ref value);
            var key1 = new DirectBuffer(TypeHelper<TKey>.EnsureFixedSize(), (byte*)keyPtr);
            var value1 = new DirectBuffer(TypeHelper<TValue>.EnsureFixedSize(), (byte*)valuePtr);
            NativeMethods.AssertExecute(NativeMethods.mdb_put(txn._impl._writeHandle, _handle,
                ref key1, ref value1,
                flags));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Put(ref DirectBuffer key, ref DirectBuffer value,
            TransactionPutOptions flags = TransactionPutOptions.None)
        {
            var key2 = key;
            var value2 = value;
            LmdbEnvironment.Write(txn =>
            {
                var k = key2;
                var v = value2;

                NativeMethods.AssertExecute(NativeMethods.sdb_put(LmdbEnvironment._handle.Handle, _handle,
                    ref k, ref v,
                    flags));
                key2 = k;
                value2 = v;
                return null;
            }, false, true);
            key = key2;
            value = value2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Put<TKey, TValue>(ReadOnlyMemory<TKey> key, ReadOnlyMemory<TValue> value,
            TransactionPutOptions flags = TransactionPutOptions.None)
            where TKey : struct where TValue : struct
        {
            LmdbEnvironment.Write(txn =>
            {
                var keyBytesSpan = MemoryMarshal.Cast<TKey, byte>(key.Span);
                var valueBytesSpan = MemoryMarshal.Cast<TValue, byte>(value.Span);
                fixed (byte* keyPtr = &MemoryMarshal.GetReference(keyBytesSpan), valuePtr = &MemoryMarshal.GetReference(valueBytesSpan))
                {
                    var key1 = new DirectBuffer(keyBytesSpan.Length, keyPtr);
                    var value1 = new DirectBuffer(valueBytesSpan.Length, valuePtr);
                    NativeMethods.AssertExecute(NativeMethods.sdb_put(LmdbEnvironment._handle.Handle, _handle,
                        ref key1, ref value1,
                        flags));
                }
                return null;
            }, false, true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Put<TKey, TValue>(TKey key, TValue value,
            TransactionPutOptions flags = TransactionPutOptions.None)
            where TKey : struct where TValue : struct
        {
            LmdbEnvironment.Write(txn =>
            {
                var keyPtr = AsPointer(ref key);
                var valuePtr = AsPointer(ref value);
                var key1 = new DirectBuffer(TypeHelper<TKey>.EnsureFixedSize(), (byte*)keyPtr);
                var value1 = new DirectBuffer(TypeHelper<TValue>.EnsureFixedSize(), (byte*)valuePtr);
                NativeMethods.AssertExecute(NativeMethods.sdb_put(LmdbEnvironment._handle.Handle, _handle,
                    ref key1, ref value1,
                    flags));

                return null;
            }, false, true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe Task PutAsync<TKey, TValue>(ReadOnlyMemory<TKey> key, ReadOnlyMemory<TValue> value,
            TransactionPutOptions flags = TransactionPutOptions.None)
            where TKey : struct where TValue : struct
        {
            return LmdbEnvironment.WriteAsync(txn =>
            {
                var keyBytesSpan = MemoryMarshal.Cast<TKey, byte>(key.Span);
                var valueBytesSpan = MemoryMarshal.Cast<TValue, byte>(value.Span);
                fixed (byte* keyPtr = &MemoryMarshal.GetReference(keyBytesSpan), valuePtr = &MemoryMarshal.GetReference(valueBytesSpan))
                {
                    var key1 = new DirectBuffer(keyBytesSpan.Length, keyPtr);
                    var value1 = new DirectBuffer(valueBytesSpan.Length, valuePtr);
                    NativeMethods.AssertExecute(NativeMethods.sdb_put(LmdbEnvironment._handle.Handle, _handle,
                        ref key1, ref value1,
                        flags));
                }
                return null;
            }, true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe Task PutAsync<TKey, TValue>(TKey key, TValue value,
            TransactionPutOptions flags = TransactionPutOptions.None)
            where TKey : struct where TValue : struct
        {
            return LmdbEnvironment.WriteAsync(txn =>
            {
                var keyPtr = AsPointer(ref key);
                var valuePtr = AsPointer(ref value);
                var key1 = new DirectBuffer(TypeHelper<TKey>.EnsureFixedSize(), (byte*)keyPtr);
                var value1 = new DirectBuffer(TypeHelper<TValue>.EnsureFixedSize(), (byte*)valuePtr);
                NativeMethods.AssertExecute(NativeMethods.sdb_put(LmdbEnvironment._handle.Handle, _handle,
                    ref key1, ref value1,
                    flags));

                return null;
            }, true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Delete(Transaction txn, ref DirectBuffer key, ref DirectBuffer value)
        {
            NativeMethods.AssertExecute(NativeMethods.mdb_del(txn._impl._writeHandle, _handle,
                ref key, ref value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Delete<TKey, TValue>(Transaction txn, ReadOnlyMemory<TKey> key, ReadOnlyMemory<TValue> value)
            where TKey : struct where TValue : struct
        {
            var keyBytesSpan = MemoryMarshal.Cast<TKey, byte>(key.Span);
            var valueBytesSpan = MemoryMarshal.Cast<TValue, byte>(value.Span);
            fixed (byte* keyPtr = &MemoryMarshal.GetReference(keyBytesSpan), valuePtr = &MemoryMarshal.GetReference(valueBytesSpan))
            {
                var key1 = new DirectBuffer(keyBytesSpan.Length, keyPtr);
                var value1 = new DirectBuffer(valueBytesSpan.Length, valuePtr);
                NativeMethods.AssertExecute(NativeMethods.mdb_del(txn._impl._writeHandle, _handle,
                    ref key1, ref value1));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Delete<TKey, TValue>(Transaction txn, TKey key, TValue value)
            where TKey : struct where TValue : struct
        {
            var keyPtr = AsPointer(ref key);
            var valuePtr = AsPointer(ref value);
            var key1 = new DirectBuffer(TypeHelper<TKey>.EnsureFixedSize(), (byte*)keyPtr);
            var value1 = new DirectBuffer(TypeHelper<TValue>.EnsureFixedSize(), (byte*)valuePtr);
            NativeMethods.AssertExecute(NativeMethods.mdb_del(txn._impl._writeHandle, _handle,
                ref key1, ref value1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(Transaction txn, ref DirectBuffer key, out DirectBuffer value)
        {
            var res = NativeMethods.AssertRead(NativeMethods.mdb_get(txn._impl._writeHandle, _handle, ref key, out value));
            return res != NativeMethods.MDB_NOTFOUND;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool TryGet<TKey, TValue>(Transaction txn, ref TKey key, out TValue value)
            where TKey : struct where TValue : struct
        {
            var keyPtr = AsPointer(ref key);
            var key1 = new DirectBuffer(TypeHelper<TKey>.EnsureFixedSize(), (byte*)keyPtr);
            TypeHelper<TValue>.EnsureFixedSize();
            var res = NativeMethods.AssertRead(NativeMethods.mdb_get(txn._impl._writeHandle, _handle,
                ref key1, out DirectBuffer value1));
            if (res != NativeMethods.MDB_NOTFOUND)
            {
                key = ReadUnaligned<TKey>((byte*)key1.Data);
                value = ReadUnaligned<TValue>((byte*)value1.Data);
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(ReadOnlyTransaction txn, ref DirectBuffer key, out DirectBuffer value)
        {
            var res = NativeMethods.AssertRead(NativeMethods.mdb_get(txn._impl._readHandle.Handle, _handle, ref key, out value));
            return res != NativeMethods.MDB_NOTFOUND;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool TryGet<TKey, TValue>(ReadOnlyTransaction txn, ref TKey key, out TValue value)
            where TKey : struct where TValue : struct
        {
            var keyPtr = AsPointer(ref key);
            var key1 = new DirectBuffer(TypeHelper<TKey>.EnsureFixedSize(), (byte*)keyPtr);
            TypeHelper<TValue>.EnsureFixedSize();
            var res = NativeMethods.AssertRead(NativeMethods.mdb_get(txn._impl._readHandle.Handle, _handle,
                ref key1, out DirectBuffer value1));
            if (res != NativeMethods.MDB_NOTFOUND)
            {
                value = ReadUnaligned<TValue>((byte*)value1.Data);
                return true;
            }

            value = default;
            return false;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_handle == default)
            {
                return;
            }
            NativeMethods.mdb_dbi_close(LmdbEnvironment._handle, _handle);
            _handle = default;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }

        ~Database()
        {
            Dispose(false);
        }
    }
}
