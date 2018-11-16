#region License

// Copyright (c) K2 Workflow (SourceCode Technology Holdings Inc.). All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

#endregion

using System;
using System.Linq;
using SourceCode.Clay.Randoms;
using Xunit;

namespace SourceCode.Clay.Tests
{
    public static class RandomSourceTests
    {
        private const int Seed = 123456789; // Specific seed for determinism
        private static readonly Random s_random = new Random(Seed); 
        private static readonly Uniform s_uniform = new Uniform(Seed);

        [Fact(DisplayName = nameof(Random_clamped_uniform))]
        public static void Random_clamped_uniform()
        {
            const int count = 100_000;
            const double min = 10;
            const double max = 1500;

            var normal = Uniform.FromRange(min, max, s_random);
            double[] values = normal.Sample(count).ToArray();

            Assert.All(values, n => Assert.True(n >= min && n <= max));

            int groupCount = values
                .GroupBy(n => n)
                .Count();

            Assert.True(groupCount >= 1000); // 1,479
        }

        [Fact(DisplayName = nameof(Random_clamped_normal))]
        public static void Random_clamped_normal()
        {
            const int count = 100_000;
            const double min = 10;
            const double max = 1500;

            var normal = Normal.FromRange(min, max, s_uniform);
            double[] values = normal.Sample(count).ToArray();

            Assert.All(values, n => Assert.True(n >= min && n <= max));

            int groupCount = values
                .GroupBy(n => n)
                .Count();

            Assert.True(groupCount >= 1000); // 1,479
        }

        [Fact(DisplayName = nameof(Random_clamped_uniform_range_zero))]
        public static void Random_clamped_uniform_range_zero()
        {
            const int count = 150_000;
            const double min = 10;
            const double max = 10;

            var normal = Uniform.FromRange(min, max, s_random);
            double[] values = normal.Sample(count).ToArray();

            Assert.All(values, n => Assert.True(n == min));
        }

        [Fact(DisplayName = nameof(Random_clamped_normal_range_zero))]
        public static void Random_clamped_normal_range_zero()
        {
            const int count = 150_000;
            const double min = 10;
            const double max = 10;

            var normal = Normal.FromRange(min, max, s_uniform);
            double[] values = normal.Sample(count).ToArray();

            Assert.All(values, n => Assert.True(n == min));
        }

        [Fact(DisplayName = nameof(Random_derive_uniform))]
        public static void Random_derive_uniform()
        {
            const int count = 100_000;
            const double μ = 100; // Mean
            const double σ = 10; // Sigma

            var normal = Uniform.FromMuSigma(μ, σ, s_random);
            double[] values = normal.Sample(count).ToArray();

            // ~99.7% of population is within +/- 3 standard deviations
            const double sd = 3 * σ;
            int cnt = values
                .Where(n => n >= μ - sd && n <= μ + sd)
                .Count();

            Assert.True(cnt >= 0.990 * count); // 99,716

            double min = values.Min();
            double avg = values.Average();
            double max = values.Max();

            Assert.True(min < avg);
            Assert.True(avg < max);
        }

        [Fact(DisplayName = nameof(Random_derive_normal))]
        public static void Random_derive_normal()
        {
            const int count = 100_000;
            const double μ = 100; // Mean
            const double σ = 10; // Sigma

            var normal = Normal.FromMuSigma(μ, σ, s_uniform);
            double[] values = normal.Sample(count).ToArray();

            // ~99.7% of population is within +/- 3 standard deviations
            const double sd = 3 * σ;
            int cnt = values
                .Where(n => n >= μ - sd && n <= μ + sd)
                .Count();

            Assert.True(cnt >= 0.990 * count); // 99,716

            double min = values.Min();
            double avg = values.Average();
            double max = values.Max();

            Assert.True(min < avg);
            Assert.True(avg < max);
        }

        [Fact(DisplayName = nameof(Random_derive_uniform_sigma_zero))]
        public static void Random_derive_uniform_sigma_zero()
        {
            const int count = 150_000;
            const double μ = 100; // Mean
            const double σ = 0; // Sigma

            var normal = Uniform.FromMuSigma(μ, σ, s_random);
            double[] values = normal.Sample(count).ToArray();

            Assert.All(values, n => Assert.True(n == μ));
        }

        [Fact(DisplayName = nameof(Random_derive_normal_sigma_zero))]
        public static void Random_derive_normal_sigma_zero()
        {
            const int count = 150_000;
            const double μ = 100; // Mean
            const double σ = 0; // Sigma

            var normal = Normal.FromMuSigma(μ, σ, s_uniform);
            double[] values = normal.Sample(count).ToArray();

            Assert.All(values, n => Assert.True(n == μ));
        }
    }
}