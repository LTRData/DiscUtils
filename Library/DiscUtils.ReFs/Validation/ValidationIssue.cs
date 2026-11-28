namespace DiscUtils.ReFs.Validation;

/// <summary>One problem found by <c>Validate</c>: where (an object, path or structure) and what.</summary>
public sealed record ValidationIssue(string Location, string Message);