using System;

namespace JetBrains.Annotations
{
    [AttributeUsage(AttributeTargets.All)]
    sealed class NotNullAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.All)]
    sealed class CanBeNullAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.All)]
    sealed class UsedImplicitlyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.All)]
    sealed class ItemNotNullAttribute : Attribute
    {
    }
}
