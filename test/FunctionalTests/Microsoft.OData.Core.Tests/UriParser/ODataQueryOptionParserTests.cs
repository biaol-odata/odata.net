﻿//---------------------------------------------------------------------
// <copyright file="ODataQueryOptionParserUnitTests.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.OData.UriParser;
using Xunit;
using ODataErrorStrings = Microsoft.OData.Strings;

namespace Microsoft.OData.Tests.UriParser
{
    /// <summary>
    /// Unit tests for ODataQueryOptionParser.
    /// </summary>
    public class ODataQueryOptionParserTests
    {
        [Fact]
        public void DefaultParameterAliasNodesShouldBeEmtpy()
        {
            var uriParser = new ODataQueryOptionParser(HardCodedTestModel.TestModel, HardCodedTestModel.GetPersonType(), HardCodedTestModel.GetPeopleSet(), new Dictionary<string, string>());
            uriParser.ParameterAliasNodes.Count.Should().Be(0);
        }

        [Fact]
        public void NullInputQueryOptionShouldThrow()
        {
            Action action = () => new ODataQueryOptionParser(HardCodedTestModel.TestModel, HardCodedTestModel.GetPersonType(), HardCodedTestModel.GetPeopleSet(), null);
            action.ShouldThrow<ArgumentNullException>().Where(e => e.Message.Contains("Value cannot be null."));
        }

        [Fact]
        public void EmptyQueryOptionDictionaryShouldWork()
        {
            var uriParser = new ODataQueryOptionParser(HardCodedTestModel.TestModel, HardCodedTestModel.GetPersonType(), HardCodedTestModel.GetPeopleSet(), new Dictionary<string, string>());
            uriParser.ParseFilter().Should().BeNull();
            uriParser.ParseSelectAndExpand().Should().BeNull();
            uriParser.ParseOrderBy().Should().BeNull();
            uriParser.ParseTop().Should().Be(null);
            uriParser.ParseSkip().Should().Be(null);
            uriParser.ParseIndex().Should().Be(null);
            uriParser.ParseCount().Should().Be(null);
            uriParser.ParseSearch().Should().BeNull();
            uriParser.ParseCompute().Should().BeNull();
        }

        [Fact]
        public void QueryOptionWithEmptyValueShouldWork()
        {
            var uriParser = new ODataQueryOptionParser(HardCodedTestModel.TestModel, HardCodedTestModel.GetPersonType(), HardCodedTestModel.GetPeopleSet(), new Dictionary<string, string>()
            {
                {"$filter"  , ""},
                {"$expand"  , ""},
                {"$select"  , ""},
                {"$orderby" , ""},
                {"$top"     , ""},
                {"$skip"    , ""},
                {"$index"   , ""},
                {"$count"   , ""},
                {"$search"  , ""},
                {"$compute" , ""},
                {"$unknow"  , ""},
            });

            uriParser.ParseFilter().Should().BeNull();
            var results = uriParser.ParseSelectAndExpand();
            results.AllSelected.Should().BeTrue();
            results.SelectedItems.Should().HaveCount(0);
            uriParser.ParseOrderBy().Should().BeNull();
            uriParser.ParseCompute().Should().BeNull();
            Action action = () => uriParser.ParseTop();
            action.ShouldThrow<ODataException>().WithMessage(Strings.SyntacticTree_InvalidTopQueryOptionValue(""));
            action = () => uriParser.ParseSkip();
            action.ShouldThrow<ODataException>().WithMessage(Strings.SyntacticTree_InvalidSkipQueryOptionValue(""));
            action = () => uriParser.ParseIndex();
            action.ShouldThrow<ODataException>().WithMessage(Strings.SyntacticTree_InvalidIndexQueryOptionValue(""));
            action = () => uriParser.ParseCount();
            action.ShouldThrow<ODataException>().WithMessage(Strings.ODataUriParser_InvalidCount(""));
            action = () => uriParser.ParseSearch();
            action.ShouldThrow<ODataException>().WithMessage(Strings.UriQueryExpressionParser_ExpressionExpected(0, ""));
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("false", false)]
        [InlineData("True", true)]
        [InlineData("False", false)]
        [InlineData("TrUe", true)]
        [InlineData("FaLsE", false)]
        public void QueryOptionCountWithCaseInsensitiveValueShouldWork(string value, bool? result)
        {
            var uriParser = new ODataQueryOptionParser(
                HardCodedTestModel.TestModel,
                HardCodedTestModel.GetPersonType(),
                HardCodedTestModel.GetPeopleSet(),
                new Dictionary<string, string>(){{"$count"   , value}})
            {
                Resolver = new ODataUriResolver { EnableCaseInsensitive = true }
            };

            uriParser.ParseCount().Should().Be(result);
        }

        [Theory]
        [InlineData("True")]
        [InlineData("False")]
        [InlineData("TrUe")]
        [InlineData("FaLsE")]
        public void QueryOptionCountWithMixedCaseValueShouldThrow(string value)
        {
            var uriParser = new ODataQueryOptionParser(
                HardCodedTestModel.TestModel,
                HardCodedTestModel.GetPersonType(),
                HardCodedTestModel.GetPeopleSet(),
                new Dictionary<string, string>(){{"$count"   , value}});

            Action action = () => uriParser.ParseCount();
            action.ShouldThrow<ODataException>().WithMessage(Strings.ODataUriParser_InvalidCount(value));
        }

        [Fact]
        public void QueryOptionWithNullValueShouldWork()
        {
            var uriParser = new ODataQueryOptionParser(HardCodedTestModel.TestModel, HardCodedTestModel.GetPersonType(), HardCodedTestModel.GetPeopleSet(), new Dictionary<string, string>()
            {
                {"$filter"  , null},
                {"$expand"  , null},
                {"$select"  , null},
                {"$orderby" , null},
                {"$top"     , null},
                {"$skip"    , null},
                {"index"    , null},
                {"$count"   , null},
                {"$search"  , null},
                {"$compute" , null},
                {"$unknow"  , null},
            });

            uriParser.ParseFilter().Should().BeNull();
            uriParser.ParseSelectAndExpand().Should().BeNull();
            uriParser.ParseOrderBy().Should().BeNull();
            uriParser.ParseTop().Should().Be(null);
            uriParser.ParseSkip().Should().Be(null);
            uriParser.ParseIndex().Should().Be(null);
            uriParser.ParseCount().Should().Be(null);
            uriParser.ParseSearch().Should().BeNull();
            uriParser.ParseCompute().Should().BeNull();
        }
    }
}
