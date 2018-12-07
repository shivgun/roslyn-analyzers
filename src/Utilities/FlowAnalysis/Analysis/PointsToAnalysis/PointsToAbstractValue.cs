﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Analyzer.Utilities;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace Microsoft.CodeAnalysis.FlowAnalysis.DataFlow.PointsToAnalysis
{
    /// <summary>
    /// Abstract PointsTo value for an <see cref="AnalysisEntity"/>/<see cref="IOperation"/> tracked by <see cref="PointsToAnalysis"/>.
    /// It contains the set of possible <see cref="AbstractLocation"/>s that the entity or the operation can point to and the <see cref="Kind"/> of the location(s).
    /// </summary>
    internal class PointsToAbstractValue : CacheBasedEquatable<PointsToAbstractValue>
    {
        public static PointsToAbstractValue Undefined = new PointsToAbstractValue(PointsToAbstractValueKind.Undefined, NullAbstractValue.Undefined);
        public static PointsToAbstractValue Invalid = new PointsToAbstractValue(PointsToAbstractValueKind.Invalid, NullAbstractValue.Invalid);
        public static PointsToAbstractValue Unknown = new PointsToAbstractValue(PointsToAbstractValueKind.Unknown, NullAbstractValue.MaybeNull);
        public static PointsToAbstractValue NoLocation = new PointsToAbstractValue(ImmutableHashSet.Create(AbstractLocation.NoLocation), NullAbstractValue.NotNull);
        public static PointsToAbstractValue NullLocation = new PointsToAbstractValue(ImmutableHashSet.Create(AbstractLocation.Null), NullAbstractValue.Null);

        private PointsToAbstractValue(ImmutableHashSet<AbstractLocation> locations, NullAbstractValue nullState)
        {
            Debug.Assert(!locations.IsEmpty);
            Debug.Assert(locations.All(location => !location.IsNull) || nullState != NullAbstractValue.NotNull);
            Debug.Assert(nullState != NullAbstractValue.Undefined);
            Debug.Assert(nullState != NullAbstractValue.Invalid);

            Locations = locations;
            LValueCapturedOperations = ImmutableHashSet<IOperation>.Empty;
            Kind = locations.Any(l => l.IsAnalysisEntityDefaultLocation) ? PointsToAbstractValueKind.Unknown : PointsToAbstractValueKind.KnownLocations;
            NullState = nullState;
        }

        private PointsToAbstractValue(ImmutableHashSet<IOperation> lValueCapturedOperations)
        {
            Debug.Assert(!lValueCapturedOperations.IsEmpty);

            LValueCapturedOperations = lValueCapturedOperations;
            Locations = ImmutableHashSet<AbstractLocation>.Empty;
            Kind = PointsToAbstractValueKind.KnownLValueCaptures;
            NullState = NullAbstractValue.NotNull;
        }

        private PointsToAbstractValue(PointsToAbstractValueKind kind, NullAbstractValue nullState)
        {
            Debug.Assert(kind != PointsToAbstractValueKind.KnownLocations);
            Debug.Assert(kind != PointsToAbstractValueKind.KnownLValueCaptures);
            Debug.Assert(nullState != NullAbstractValue.Null);

            Locations = ImmutableHashSet<AbstractLocation>.Empty;
            LValueCapturedOperations = ImmutableHashSet<IOperation>.Empty;
            Kind = kind;
            NullState = nullState;
        }

        public static PointsToAbstractValue Create(AbstractLocation location, bool mayBeNull)
        {
            Debug.Assert(!location.IsNull, "Use 'PointsToAbstractValue.NullLocation' singleton");
            Debug.Assert(!location.IsNoLocation, "Use 'PointsToAbstractValue.NoLocation' singleton");

            return new PointsToAbstractValue(ImmutableHashSet.Create(location), mayBeNull ? NullAbstractValue.MaybeNull : NullAbstractValue.NotNull);
        }

        public static PointsToAbstractValue Create(IOperation lValueCapturedOperation)
        {
            Debug.Assert(lValueCapturedOperation != null);
            return new PointsToAbstractValue(ImmutableHashSet.Create(lValueCapturedOperation));
        }

        public static PointsToAbstractValue Create(ImmutableHashSet<AbstractLocation> locations, NullAbstractValue nullState)
        {
            Debug.Assert(!locations.IsEmpty);

            if (locations.Count == 1)
            {
                var location = locations.Single();
                if (location.IsNull)
                {
                    return NullLocation;
                }
                if (location.IsNoLocation)
                {
                    return NoLocation;
                }
            }

            return new PointsToAbstractValue(locations, nullState);
        }

        public static PointsToAbstractValue Create(ImmutableHashSet<IOperation> lValueCapturedOperations)
        {
            Debug.Assert(!lValueCapturedOperations.IsEmpty);
            return new PointsToAbstractValue(lValueCapturedOperations);
        }

        public PointsToAbstractValue MakeNonNull(IOperation operation, PointsToAnalysisContext analysisContext, AnalysisEntity analysisEntityForOperationOpt)
        {
            Debug.Assert(Kind != PointsToAbstractValueKind.KnownLValueCaptures);

            if (NullState == NullAbstractValue.NotNull)
            {
                return this;
            }

            if (Locations.IsEmpty)
            {
                var location = analysisEntityForOperationOpt != null ?
                    AbstractLocation.CreateAnalysisEntityDefaultLocation(analysisEntityForOperationOpt) :
                    AbstractLocation.CreateAllocationLocation(operation, operation.Type, analysisContext);
                return Create(location, mayBeNull: analysisEntityForOperationOpt != null);
            }

            var locations = Locations.Where(location => !location.IsNull).ToImmutableHashSet();
            if (locations.Count == Locations.Count)
            {
                locations = Locations;
            }

            return new PointsToAbstractValue(locations, NullAbstractValue.NotNull);
        }

        public PointsToAbstractValue MakeNull(AnalysisEntity analysisEntityForOperationOpt)
        {
            Debug.Assert(Kind != PointsToAbstractValueKind.KnownLValueCaptures);

            if (NullState == NullAbstractValue.Null)
            {
                return this;
            }

            return MakeNull(Locations, analysisEntityForOperationOpt);
        }

        private static PointsToAbstractValue MakeNull(ImmutableHashSet<AbstractLocation> locations, AnalysisEntity analysisEntityForOperationOpt)
        {
            if (locations.IsEmpty)
            {
                if (analysisEntityForOperationOpt == null)
                {
                    return NullLocation;
                }

                var location = AbstractLocation.CreateAnalysisEntityDefaultLocation(analysisEntityForOperationOpt);
                locations = ImmutableHashSet.Create(location);
            }

            return new PointsToAbstractValue(locations, NullAbstractValue.Null);
        }

        public PointsToAbstractValue MakeMayBeNull(AnalysisEntity analysisEntityForOperationOpt)
        {
            Debug.Assert(Kind != PointsToAbstractValueKind.KnownLValueCaptures);
            Debug.Assert(NullState != NullAbstractValue.Null);

            if (NullState == NullAbstractValue.MaybeNull)
            {
                return this;
            }

            return MakeMayBeNull(Locations, analysisEntityForOperationOpt);
        }

        private static PointsToAbstractValue MakeMayBeNull(ImmutableHashSet<AbstractLocation> locations, AnalysisEntity analysisEntityForOperationOpt)
        {
            if (locations.IsEmpty)
            {
                if (analysisEntityForOperationOpt == null)
                {
                    return Unknown;
                }

                var location = AbstractLocation.CreateAnalysisEntityDefaultLocation(analysisEntityForOperationOpt);
                locations = ImmutableHashSet.Create(location);
            }

            Debug.Assert(locations.All(location => !location.IsNull));
            return new PointsToAbstractValue(locations, NullAbstractValue.MaybeNull);
        }

        public ImmutableHashSet<AbstractLocation> Locations { get; }
        public ImmutableHashSet<IOperation> LValueCapturedOperations { get; }
        public PointsToAbstractValueKind Kind { get; }
        public NullAbstractValue NullState { get; }

        protected override void ComputeHashCodeParts(ImmutableArray<int>.Builder builder)
        {
            builder.Add(HashUtilities.Combine(Locations));
            builder.Add(HashUtilities.Combine(LValueCapturedOperations));
            builder.Add(Kind.GetHashCode());
            builder.Add(NullState.GetHashCode());
        }
    }
}