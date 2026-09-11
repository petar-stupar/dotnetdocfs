namespace DotnetDocFs.Catalog.Internal.Metadata;

/// <summary>
/// One overload: the C# declaration a reader recognises, and the documentation id the XML file
/// keys its prose by.
/// </summary>
/// <param name="Declaration">The declaration as C# spells it, without a body.</param>
/// <param name="DocId">The ECMA-335 documentation id of this exact overload.</param>
internal sealed record MemberSignature(string Declaration, string DocId);
