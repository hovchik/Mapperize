// Example: map into an existing instance ("update").
//
// Declare a method that takes the source AND the target. Mapperize assigns each mapped member onto
// the existing target instead of allocating a new object. Return `void`, or return the target for
// fluent chaining. Init-only members are left untouched (they can only be set at construction).
using Mapperize;

var mapper = new ProfileMapper();

// void update: mutate the target in place, preserving members you don't map.
var profile = new Profile { DisplayName = "old", Bio = "keep this bio", LastSeen = DateTime.UnixEpoch };
mapper.Update(new ProfileEdit { DisplayName = "Ada" }, profile);

Console.WriteLine($"After Update  => DisplayName='{profile.DisplayName}', Bio='{profile.Bio}'");

// fluent update: same behavior, but returns the target so you can chain.
var chained = mapper.Merge(new ProfileEdit { DisplayName = "Grace" }, profile);
Console.WriteLine($"After Merge   => same instance? {ReferenceEquals(chained, profile)}, DisplayName='{chained.DisplayName}'");

return profile.Bio == "keep this bio" && ReferenceEquals(chained, profile) && chained.DisplayName == "Grace" ? 0 : 1;

// ---- mapper ----------------------------------------------------------------

[Mapper(UnmappedMemberBehavior = UnmappedMemberBehavior.Ignore)]
public partial class ProfileMapper
{
    public partial void Update(ProfileEdit source, Profile target);       // populate in place
    public partial Profile Merge(ProfileEdit source, Profile target);     // ...and return it
}

// ---- models ----------------------------------------------------------------

public class ProfileEdit
{
    public string DisplayName { get; set; } = "";
}

public class Profile
{
    public string DisplayName { get; set; } = "";
    public string Bio { get; set; } = "";        // not present on the edit -> left as-is
    public DateTime LastSeen { get; init; }       // init-only -> never touched by an update
}
