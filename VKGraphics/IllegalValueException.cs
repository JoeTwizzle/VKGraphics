using System.Diagnostics.CodeAnalysis;

namespace VKGraphics;

internal static class Illegal
{
    [DoesNotReturn]
    internal static void Handle<T>()
    {
        throw new IllegalValueException<T>();
    }

    [DoesNotReturn]
    internal static R Handle<T, R>()
    {
        throw new IllegalValueException<T, R>();
    }

    internal class IllegalValueException<T> : VeldridException
    {
    }

    internal class IllegalValueException<T, R> : IllegalValueException<T>
    {
    }
}
