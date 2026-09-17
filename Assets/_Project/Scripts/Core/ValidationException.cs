using System;

namespace Safehouse.Core
{
    /// <summary>
    /// Data or a save file cannot be used without correction. Thrown before anything is written,
    /// so a rejected value always leaves the previous state intact.
    /// </summary>
    public sealed class ValidationException : Exception
    {
        public ValidationException(string message) : base(message) { }
    }
}
