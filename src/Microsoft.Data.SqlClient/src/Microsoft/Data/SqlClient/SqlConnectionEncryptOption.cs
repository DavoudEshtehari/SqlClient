// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel;
using Microsoft.Data.Common;

namespace Microsoft.Data.SqlClient
{
    internal sealed class SqlConnectionEncryptOption
    {
        private const string TRUE = "True";
        private const string FALSE = "False";
        private const string STRICT = "Strict";
        private const string TRUE_LOWER = "true";
        private const string YES_LOWER = "yes";
        private const string MANDATORY_LOWER = "mandatory";
        private const string FALSE_LOWER = "false";
        private const string NO_LOWER = "no";
        private const string OPTIONAL_LOWER = "optional";
        private const string STRICT_LOWER = "strict";
        private readonly string _value;
        private static readonly SqlConnectionEncryptOption s_optional = new(FALSE);
        private static readonly SqlConnectionEncryptOption s_mandatory = new(TRUE);
        private static readonly SqlConnectionEncryptOption s_strict = new(STRICT);

        private SqlConnectionEncryptOption(string value)
        {
            _value = value;
        }

        public static SqlConnectionEncryptOption Parse(string value)
        {
            if (TryParse(value, out SqlConnectionEncryptOption result))
            {
                return result;
            }
            else
            {
                throw ADP.InvalidConnectionOptionValue(SqlConnectionString.KEY.Encrypt);
            }
        }

        internal static SqlConnectionEncryptOption Parse(bool value)
        {
            return value ? Mandatory : Optional;
        }

        public static bool TryParse(string value, out SqlConnectionEncryptOption result)
        {
            switch (value?.ToLower())
            {
                case TRUE_LOWER:
                case YES_LOWER:
                case MANDATORY_LOWER:
                    {
                        result = Mandatory;
                        return true;
                    }
                case FALSE_LOWER:
                case NO_LOWER:
                case OPTIONAL_LOWER:
                    {
                        result = Optional;
                        return true;
                    }
                case STRICT_LOWER:
                    {
                        result = Strict;
                        return true;
                    }
                default:
                    result = null;
                    return false;
            }
        }

        public static SqlConnectionEncryptOption Optional => s_optional;

        public static SqlConnectionEncryptOption Mandatory => s_mandatory;

        public static SqlConnectionEncryptOption Strict => s_strict;

        public static implicit operator SqlConnectionEncryptOption(bool value) => value ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional;

        public static implicit operator bool(SqlConnectionEncryptOption value) => !Optional.Equals(value);

        public override string ToString() => _value;

        public override bool Equals(object obj)
        {
            if (obj != null &&
                obj is SqlConnectionEncryptOption option)
            {
                return ToString().Equals(option.ToString());
            }

            return false;
        }

        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }
    }
}
