Param(
    [string]$remotePath,
    [string]$localPath
)

# Create remote
cd $remotePath
git init --bare 

#Create local

cd $localPath
git init
git add *
git commit -m 'First Commit'
git remote add origin $remotePath
git push --set-upstream origin master
