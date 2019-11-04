# Creating A Brand New World

A World is stored in a repository. You can use a existing repository that already host a world, or you can create an empty repository.

## `develop` required

Currently, git repositories are checked out on the `develop` branch instead of their default one. Be sure that all the repositories you will add in your world have a `develop` branch or CKli will fail on checkout.

## Importing an Untooled multi-repositories Stack

If you get multiple repositories, and want to use CKli on them, a bit of work is required.

### Create the World Config

The first step is to create a new World config, and add the repositories to the world.

Heads to [Edit A World](EditAWorld.md) documentation, to configurate properly your World.

Currently, CKli require:

- A Visual Studio Solution, at the root of the repository.

- The CodeCakeBuilder CI script
  
  - located in a folder named "CodeCakeBuilder" at the root of the repository
  
  - Referenced in the Solution previously mentioned


