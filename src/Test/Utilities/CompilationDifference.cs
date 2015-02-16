﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.
extern alias PDB;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CodeGen;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Test.Utilities;
using PDB::Roslyn.Test.MetadataUtilities;
using PDB::Roslyn.Test.PdbUtilities;
using Roslyn.Test.Utilities;

namespace Microsoft.CodeAnalysis.Test.Utilities
{
    internal sealed class CompilationDifference
    {
        public readonly ImmutableArray<byte> MetadataDelta;
        public readonly ImmutableArray<byte> ILDelta;
        public readonly Stream PdbDelta;
        public readonly CompilationTestData TestData;
        public readonly EmitDifferenceResult EmitResult;
        public readonly ImmutableArray<MethodDefinitionHandle> UpdatedMethods;

        public CompilationDifference(
            ImmutableArray<byte> metadata,
            ImmutableArray<byte> il,
            Stream pdbStream,
            CompilationTestData testData,
            EmitDifferenceResult result,
            ImmutableArray<MethodDefinitionHandle> methodHandles)
        {
            this.MetadataDelta = metadata;
            this.ILDelta = il;
            this.PdbDelta = pdbStream;
            this.TestData = testData;
            this.EmitResult = result;
            this.UpdatedMethods = methodHandles;
        }

        public EmitBaseline NextGeneration
        {
            get
            {
                return EmitResult.Baseline;
            }
        }

        public PinnedMetadata GetMetadata()
        {
            return new PinnedMetadata(MetadataDelta);
        }

        public void VerifyIL(
            string expectedIL)
        {
            string actualIL = ILDelta.GetMethodIL();
            AssertEx.AssertEqualToleratingWhitespaceDifferences(expectedIL, actualIL, escapeQuotes: true, expectedValueSourcePath: null, expectedValueSourceLine: 0);
        }

        public void VerifyIL(
            string qualifiedMethodName,
            string expectedIL,
            Func<Cci.ILocalDefinition, ILVisualizer.LocalInfo> mapLocal = null,
            MethodDefinitionHandle methodToken = default(MethodDefinitionHandle),
            [CallerFilePath]string callerPath = null,
            [CallerLineNumber]int callerLine = 0)
        {
            var ilBuilder = TestData.GetMethodData(qualifiedMethodName).ILBuilder;

            Dictionary<int, string> sequencePointMarkers = null;
            if (!methodToken.IsNil)
            {
                string actualPdb = PdbToXmlConverter.DeltaPdbToXml(PdbDelta, new[] { MetadataTokens.GetToken(methodToken) });
                sequencePointMarkers = TestBase.GetMarkers(actualPdb);
            }

            string actualIL = ILBuilderVisualizer.ILBuilderToString(ilBuilder, mapLocal ?? ToLocalInfo, sequencePointMarkers);
            AssertEx.AssertEqualToleratingWhitespaceDifferences(expectedIL, actualIL, escapeQuotes: true, expectedValueSourcePath: callerPath, expectedValueSourceLine: callerLine);
        }

        public void VerifyPdb(IEnumerable<MethodDefinitionHandle> methodHandles, string expectedPdb)
        {
            VerifyPdb(methodHandles.Select(h => MetadataTokens.GetToken(h)), expectedPdb);
        }

        public void VerifyPdb(IEnumerable<int> methodTokens, string expectedPdb)
        {
            string actualPdb = PdbToXmlConverter.DeltaPdbToXml(PdbDelta, methodTokens);
            TestBase.AssertXmlEqual(expectedPdb, actualPdb);
        }

        internal string GetMethodIL(string qualifiedMethodName)
        {
            return ILBuilderVisualizer.ILBuilderToString(this.TestData.GetMethodData(qualifiedMethodName).ILBuilder, ToLocalInfo);
        }

        private static ILVisualizer.LocalInfo ToLocalInfo(Cci.ILocalDefinition local)
        {
            var signature = local.Signature;
            if (signature == null)
            {
                return new ILVisualizer.LocalInfo(local.Name, local.Type, local.IsPinned, local.IsReference);
            }
            else
            {
                // Decode simple types only.
                var typeName = (signature.Length == 1) ? GetTypeName((SignatureTypeCode)signature[0]) : null;
                return new ILVisualizer.LocalInfo(null, typeName ?? "[unchanged]", false, false);
            }
        }

        private static string GetTypeName(SignatureTypeCode typeCode)
        {
            switch (typeCode)
            {
                case SignatureTypeCode.Boolean: return "[bool]";
                case SignatureTypeCode.Int32: return "[int]";
                case SignatureTypeCode.String: return "[string]";
                case SignatureTypeCode.Object: return "[object]";
                default: return null;
            }
        }

        public void VerifySynthesizedMembers(params string[] expectedSynthesizedTypesAndMemberCounts)
        {
            var actual = EmitResult.Baseline.SynthesizedMembers.Select(e => e.Key.ToString() + ": {" + string.Join(", ", e.Value.Select(v => v.Name)) + "}");
            AssertEx.SetEqual(expectedSynthesizedTypesAndMemberCounts, actual, itemSeparator: "\r\n");
        }
    }
}
