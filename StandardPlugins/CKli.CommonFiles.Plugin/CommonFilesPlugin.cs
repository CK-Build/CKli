using CKli.Core;
using CKli.BranchModel.Plugin;

namespace CKli.CommonFiles.Plugin;

public sealed class CommonFilesPlugin : PrimaryPluginBase
{
    readonly BranchModelPlugin _branchModel;

    public CommonFilesPlugin( PrimaryPluginContext primaryContext, BranchModelPlugin branchModel )
        : base( primaryContext )
    {
        _branchModel = branchModel;
        _branchModel.ContentIssue += ContentIssueRequested;
    }

    void ContentIssueRequested( ContentIssueEvent ev )
    {
    }
}
