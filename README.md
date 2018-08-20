# Spreads.LMDB

Low-level zero-overhead and the fastest LMDB .NET wrapper with some additional native 
methods useful for [Spreads](https://github.com/Spreads/).

Available on NuGet as [Spreads.LMDB](https://www.nuget.org/packages/Spreads.LMDB). (Stale, ping the author if you want the latest version published)

## Full C# `async/await` support

LMDB's supported "normal" case is when a transaction is executed from a single thread. For .NET this means 
that if all operations on a transactions are called from a single thread it doesn't matter which
thread is executing a transaction and LMDB will just work.

In some cases one my need background execution of write transactions or .NET async operations inside LMDB transactions. For this case Spreads.LMDB
fully supports async/await. Write transactions are executed in a single thread via a blocking concurrent queue. Read transactions could be used from async code, which requires forcing [`MDB_NOTLS`](http://www.lmdb.tech/doc/group__mdb.html#ga32a193c6bf4d7d5c5d579e71f22e9340) 
attribute for environments:

> A thread may use parallel read-only transactions. A read-only transaction may span threads if the user synchronizes its use. Applications that multiplex many user threads over individual OS threads need this option. Such an application must also serialize the write transactions in an OS thread, since LMDB's write locking is unaware of the user threads.

Spreads.LMDB automatically takes care or read-only transactions and cursors renewal 
if they are properly disposed as .NET objects. It does not allocate those 
objects in steady state (uses internal pools).

**Warning!** This library exposes `MDB_val` directly as `DirectBuffer` struct, the struct *MUST ONLY* be read when inside a transaction
(or when it points to an overflow page - but that is a undocumented hack working so far). For writes, 
the memory behind Span *MUST BE pinned*.

## Generic key/values support

Any fixed-sized `unmanaged` structs could be used directly as keys/values. Until `unmanaged`
constraint and blittable helpers (at least `IsBlittable`) are widly available we use
opt-in to treat a *custom user-defined* struct as blittable. It must have explicit `Size`
parameter in `[StructLayout(LayoutKind.Sequential, Size = XX)]` or defined Spreads' 
[`SerializationAttribute`](https://github.com/Spreads/Spreads/blob/master/src/Spreads.Core/Serialization/SerializationAttribute.cs)
with `BlittableSize` parameter for non-generic types or `PreferBlittable` set to `true`
for generic types that could be blittable depending on a concrete type. The logic to decide
if a type is fixed-size is in [TypeHelper<T>](https://github.com/Spreads/Spreads/blob/master/src/Spreads.Core/Serialization/TypeHelper.cs)
and its `TypeHelper<T>.Size` static property must be positive.


# Example

There are a couple of tests that show how to use the code.

# Limitations & status

This is being deployed and tested in production. I needed a zero-overhead but convenient wrapper,
not raw P/Invoke. [`Span<T>` et al.](https://msdn.microsoft.com/en-us/magazine/mt814808.aspx) are perfect
for this!

The project has required binaries in `lib` folder - they are native dlls compressed with 
`deflate` and embedded into the package dll as resources (this often simplifies deployment). 
Source code maybe added later if someone needs it. Should work with original native binaries as well
if not using two `TryFind` helper methods.

# Contributing

Issues & PRs are welcome!

# Copyright

MPL 2.0
(c) Victor Baybekov, 2018

