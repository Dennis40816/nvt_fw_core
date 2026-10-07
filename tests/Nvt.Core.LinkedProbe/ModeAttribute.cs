// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.LinkedProbe;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class LinkedProbeModeAttribute(string name) : Attribute
{
    internal string Name { get; } = name;
}
