#region License

// Copyright (c) K2 Workflow (SourceCode Technology Holdings Inc.). All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SourceCode.Clay
{
    public abstract class RandomDistribution
    {
        /// <summary>
        /// A default shared instance to use for Uniform distributions, in the range [0, 1).
        /// </summary>
        public static UniformDistribution Uniform { get; } = UniformDistribution.FromRange(0, 1);

        /// <summary>
        /// A default shared instance to use for Normal (Gauss) distributions, in the range [0, 1).
        /// </summary>
        public static NormalDistribution Normal { get; } = NormalDistribution.FromRange(0, 1);

        // No need to be ThreadStatic - accessed with a lock
        private static readonly Random s_random = new Random();
        private readonly Random _random;

        protected ClampInfo Clamp { get; }

        protected RandomDistribution(ClampInfo clamp, Random random)
        {
            _random = random ?? s_random;
            Clamp = clamp;
        }

        protected double SafeDouble()
        {
            // https://stackoverflow.com/questions/25448070/getting-random-numbers-in-a-thread-safe-way/25448166#25448166
            // https://docs.microsoft.com/en-us/dotnet/api/system.random?view=netframework-4.7.2#the-systemrandom-class-and-thread-safety
            lock (_random)
            {
                return _random.NextDouble();
            }
        }

        /// <summary>
        /// Returns the next random number within the specified range and distribution.
        /// </summary>
        public abstract double NextDouble();

        /// <summary>
        /// Returns a sequence of random numbers within the specified range and distribution.
        /// </summary>
        /// <param name="count">The number of samples to generate.</param>
        public abstract IEnumerable<double> Sample(int count);

        #region Nested

        protected sealed class ClampInfo
        {
            public double Min { get; }

            public double Max { get; }

            public double Range { get; }

            public ClampInfo(double min, double max)
            {
                Debug.Assert(min <= max);
                Debug.Assert(!double.IsInfinity(max - min));

                Min = min;
                Max = max;
                Range = max - min;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public double Constrain(double value)
            {
                value = Math.Max(Min, value); // Floor
                value = Math.Min(Max, value); // Ceiling

                return value;
            }
        }

        #endregion
    }
}
