namespace CKli.VersionTag.Plugin;

enum TagConflict
{
    None,
    DuplicateInvalidTag,
    InvalidTagOnWrongCommit,
    SameVersionOnDifferentCommit,
    CI0VersionOnOtherCommit,
    DuplicatedVersionTag
}
