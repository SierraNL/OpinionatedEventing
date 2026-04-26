using BenchmarkDotNet.Running;
using OpinionatedEventing.Benchmarks;

BenchmarkRunner.Run<DispatchBenchmark>(args: args);
