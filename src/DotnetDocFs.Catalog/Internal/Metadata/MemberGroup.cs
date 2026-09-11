namespace DotnetDocFs.Catalog.Internal.Metadata;

/// <summary>
/// Every member of a type that shares one name, which is one page of the tree. Overloads belong
/// together: a reader asking what <c>String.Compare</c> does wants all nine of them at once, not
/// nine files to open.
/// </summary>
/// <param name="Name">The name as metadata spells it.</param>
/// <param name="PathName">The file stem this group is served under, without the extension.</param>
/// <param name="Kind">What sort of member these are.</param>
/// <param name="Signatures">The overloads, in declaration order.</param>
internal sealed record MemberGroup(
    string Name,
    string PathName,
    MemberSort Kind,
    IReadOnlyList<MemberSignature> Signatures);
