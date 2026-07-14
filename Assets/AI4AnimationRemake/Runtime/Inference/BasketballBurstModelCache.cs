using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Owns one native copy of the immutable Basketball model for every loaded
    /// model asset. Burst agents share these arrays and only keep recurrent and
    /// scratch buffers per agent.
    /// </summary>
    internal sealed class BasketballBurstModelData : IDisposable
    {
        public NativeArray<float> XMean;
        public NativeArray<float> XStd;
        public NativeArray<float> YMean;
        public NativeArray<float> YStd;

        public NativeArray<float> GatingW0;
        public NativeArray<float> GatingB0;
        public NativeArray<float> GatingW1;
        public NativeArray<float> GatingB1;
        public NativeArray<float> GatingW2;
        public NativeArray<float> GatingB2;

        public NativeArray<float> ExpertW0;
        public NativeArray<float> ExpertB0;
        public NativeArray<float> ExpertW1;
        public NativeArray<float> ExpertB1;
        public NativeArray<float> ExpertW2;
        public NativeArray<float> ExpertB2;

        public BasketballBurstModelData(BasketballModelAsset model)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }
            if (!model.Validate(out string reason))
            {
                throw new InvalidOperationException($"Invalid BasketballModelAsset: {reason}");
            }

            try
            {
                XMean = Copy(Require(model, "Xmean"));
                XStd = Copy(Require(model, "Xstd"));
                YMean = Copy(Require(model, "Ymean"));
                YStd = Copy(Require(model, "Ystd"));

                GatingW0 = Copy(Require(model, "wc000_w"));
                GatingB0 = Copy(Require(model, "wc000_b"));
                GatingW1 = Copy(Require(model, "wc010_w"));
                GatingB1 = Copy(Require(model, "wc010_b"));
                GatingW2 = Copy(Require(model, "wc020_w"));
                GatingB2 = Copy(Require(model, "wc020_b"));

                ExpertW0 = CopyExperts(
                    model, "wc10", "_w",
                    512 * BasketballModelAsset.MainFeatureCount);
                ExpertB0 = CopyExperts(model, "wc10", "_b", 512);
                ExpertW1 = CopyExperts(model, "wc11", "_w", 512 * 512);
                ExpertB1 = CopyExperts(model, "wc11", "_b", 512);
                ExpertW2 = CopyExperts(
                    model, "wc12", "_w",
                    BasketballModelAsset.OutputFeatureCount * 512);
                ExpertB2 = CopyExperts(
                    model, "wc12", "_b",
                    BasketballModelAsset.OutputFeatureCount);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            DisposeArray(ref XMean);
            DisposeArray(ref XStd);
            DisposeArray(ref YMean);
            DisposeArray(ref YStd);
            DisposeArray(ref GatingW0);
            DisposeArray(ref GatingB0);
            DisposeArray(ref GatingW1);
            DisposeArray(ref GatingB1);
            DisposeArray(ref GatingW2);
            DisposeArray(ref GatingB2);
            DisposeArray(ref ExpertW0);
            DisposeArray(ref ExpertB0);
            DisposeArray(ref ExpertW1);
            DisposeArray(ref ExpertB1);
            DisposeArray(ref ExpertW2);
            DisposeArray(ref ExpertB2);
        }

        private static float[] Require(BasketballModelAsset model, string id)
        {
            if (!model.TryGetBuffer(id, out float[] values))
            {
                throw new InvalidOperationException($"Required model buffer '{id}' is missing.");
            }
            return values;
        }

        private static NativeArray<float> Copy(float[] source)
        {
            NativeArray<float> destination = new(
                source.Length,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            for (int index = 0; index < source.Length; index++)
            {
                destination[index] = source[index];
            }
            return destination;
        }

        private static NativeArray<float> CopyExperts(
            BasketballModelAsset model,
            string prefix,
            string suffix,
            int valuesPerExpert)
        {
            NativeArray<float> destination = new(
                BasketballModelAsset.ExpertCount * valuesPerExpert,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            try
            {
                for (int expert = 0; expert < BasketballModelAsset.ExpertCount; expert++)
                {
                    float[] source = Require(model, $"{prefix}{expert}{suffix}");
                    if (source.Length != valuesPerExpert)
                    {
                        throw new InvalidOperationException(
                            $"Buffer '{prefix}{expert}{suffix}' has {source.Length} values; " +
                            $"expected {valuesPerExpert}.");
                    }
                    int offset = expert * valuesPerExpert;
                    for (int index = 0; index < valuesPerExpert; index++)
                    {
                        destination[offset + index] = source[index];
                    }
                }
                return destination;
            }
            catch
            {
                destination.Dispose();
                throw;
            }
        }

        private static void DisposeArray(ref NativeArray<float> values)
        {
            if (values.IsCreated)
            {
                values.Dispose();
            }
            values = default;
        }
    }

    internal static class BasketballBurstModelCache
    {
        private sealed class Entry
        {
            public BasketballBurstModelData Data;
            public int ReferenceCount;
        }

        private static readonly Dictionary<BasketballModelAsset, Entry> Entries = new();

        public static BasketballBurstModelData Acquire(BasketballModelAsset model)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            if (Entries.TryGetValue(model, out Entry entry))
            {
                entry.ReferenceCount++;
                return entry.Data;
            }

            BasketballBurstModelData data = new(model);
            Entries.Add(model, new Entry
            {
                Data = data,
                ReferenceCount = 1
            });
            return data;
        }

        public static void Release(BasketballModelAsset model, BasketballBurstModelData data)
        {
            if (ReferenceEquals(model, null) || data == null ||
                !Entries.TryGetValue(model, out Entry entry) ||
                !ReferenceEquals(entry.Data, data))
            {
                return;
            }

            entry.ReferenceCount--;
            if (entry.ReferenceCount > 0)
            {
                return;
            }

            Entries.Remove(model);
            entry.Data.Dispose();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            foreach (Entry entry in Entries.Values)
            {
                entry.Data.Dispose();
            }
            Entries.Clear();
        }
    }
}
