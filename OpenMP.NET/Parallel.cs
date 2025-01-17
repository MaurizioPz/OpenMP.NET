﻿using System;
using OpenMP;
using System.Collections.Generic;
using System.Threading;

namespace OpenMP
{
    public static class Parallel
    {
        public enum Schedule { Static, Dynamic, Guided };
        private static Dictionary<Action, (int, object)> critical_lock = new Dictionary<Action, (int, object)>();
        private static int found_criticals = 0;
        private static volatile Barrier barrier;

        private static void FixArgs(int start, int end, Schedule sched, ref uint? chunk_size, int num_threads)
        {
            if (chunk_size == null)
            {
                switch (sched)
                {
                    case Schedule.Static:
                        chunk_size = (uint)((end - start) / num_threads);
                        break;
                    case Schedule.Dynamic:
                        chunk_size = (uint)((end - start) / num_threads) / 32;
                        if (chunk_size < 1) chunk_size = 1;
                        break;
                    case Schedule.Guided:
                        chunk_size = 1;
                        break;
                }
            }
        }

        public static void For(int start, int end, Action<int> action, Schedule schedule = Schedule.Static, uint? chunk_size = null)
        {
            FixArgs(start, end, schedule, ref chunk_size, GetNumThreads());

            if (GetThreadNum() == 0)
            {
                Init.ws = new WorkShare((uint)GetNumThreads(), ForkedRegion.ws.threads);
                Init.ws.start = start;
                Init.ws.end = end;
                Init.ws.chunk_size = chunk_size.Value;
                Init.ws.omp_fn = action;
            }

            Barrier();

            switch (schedule)
            {
                case Schedule.Static:
                    Iter.StaticLoop(GetThreadNum());
                    break;
                case Schedule.Dynamic:
                    Iter.DynamicLoop(GetThreadNum());
                    break;
                case Schedule.Guided:
                    Iter.GuidedLoop(GetThreadNum());
                    break;
            }

            Barrier();
        }

        public static void ParallelRegion(Action action, uint? num_threads = null)
        {
            num_threads ??= (uint)GetNumProcs();

            ForkedRegion.CreateThreadpool(num_threads.Value, action);
            barrier = new Barrier((int)num_threads.Value);
            ForkedRegion.StartThreadpool();
            ForkedRegion.ws.num_threads = 1;
        }

        public static void ParallelFor(int start, int end, Action<int> action, Schedule schedule = Schedule.Static, uint? chunk_size = null, uint? num_threads = null)
        {
            num_threads ??= (uint)GetNumProcs();

            ParallelRegion(num_threads: num_threads.Value, action: () =>
            {
                For(start, end, action, schedule, chunk_size);
            });
        }

        public static int Critical(Action action)
        {
            int id;
            object lock_obj;

            lock (critical_lock)
            {
                if (!critical_lock.ContainsKey(action))
                {
                    critical_lock.Add(action, (++found_criticals, new object()));
                }

                (id, lock_obj) = critical_lock[action];
            }

            lock (lock_obj)
            {
                action();
            }

            return id;
        }

        public static void Barrier()
        {
            barrier.SignalAndWait();
        }

        public static int GetNumProcs()
        {
            return Environment.ProcessorCount;
        }

        public static int GetNumThreads()
        {
            int num_threads = (int)ForkedRegion.ws.num_threads;

            if (num_threads == 0)
            {
                ForkedRegion.ws.num_threads = 1;
                return 1;
            }

            return num_threads;
        }

        public static int GetThreadNum()
        {
            return Convert.ToInt32(Thread.CurrentThread.Name);
        }

        public static void __reset_lambda_memory()
        {
            critical_lock.Clear();
            found_criticals = 0;
        }
    }
}