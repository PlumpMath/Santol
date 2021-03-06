﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mono.Cecil;

namespace Santol
{
    public static class TypeHelper
    {
        public static TypeReference GetSimplestType(TypeReference t1, TypeReference t2)
        {
            if (t1.MetadataType == MetadataType.Boolean && t2.MetadataType == MetadataType.Int32)
                return t1;
            throw new NotImplementedException("Proper simplest type finding not implemented");
        }

        public static TypeReference GetMostComplexType(TypeReference t1, TypeReference t2)
        {
            if (t1.Equals(t2))
                return t1;
            if (t1.MetadataType == MetadataType.Boolean && t2.MetadataType == MetadataType.Int32)
                return t2;
            if (t1.MetadataType == MetadataType.Char && t2.MetadataType == MetadataType.Int32)
                return t2;
            throw new NotImplementedException("Proper most complex type finding not implemented " + t1 + " " + t2);
        }

        public static bool Is(this TypeDefinition def, TypeDefinition other) => def.GetName().Equals(other.GetName());

        public static bool HasParent(this TypeDefinition type) => type.BaseType != null && !type.GetName().Equals("System_Object");

        public static bool IsParent(this TypeDefinition def, TypeDefinition other)
        {
            if (def.BaseType == null)
                return false;
            return def.BaseType.Resolve().Is(other) || def.BaseType.Resolve().IsParent(other);
        }

        public static IList<FieldDefinition> GetLocals(this TypeDefinition type)
        {
            return type.Fields.Where(fieldDefinition => !fieldDefinition.IsStatic).ToList();
        }

        public static bool ImplicitThis(this MethodReference meth) => meth.HasThis && !meth.ExplicitThis;
    }
}