# How To: Edit A World

## Config Location

The worlds are versioned in a Git Repository, if you have ensured your repository, the repository is located in the folder where CKli store all its stuff.
The CKli data path is OS specific:

- On Windows: this directory is `%LOCALAPPDATA%/CKli`. 

Anatomy of a CKli directory:

```
<DIR>          .
<DIR>          ..
           681 CKLI-INVENIETIS.KeyVault.txt
<DIR>          invenietis_cofely_engie-stack
<DIR>          Logs
<DIR>          signature-code_signature-code-stack
<DIR>          signature-mosaic_signature-mosaic-stack
<DIR>          signature-opensource_ck-stack
           798 Stacks.txt
           313 WorldLocalMapping.txt
```

Your world is located in one of these directory.

For this example, we will edit the world CK, located in the directory signature-opensource_ck-stack, we search for the file named `[STACK-NAME].World.xml`.

## Before you edit the config:

:warning: Before starting to configure, you must know that:

- The elements represent services in a service container.

- **Ordering Matter**: Services have dependencies, dependencies must be declared before, like a lot of declarative languages.

- **Scopes**: Some Services provides a scope, and services declared in it are not accessible from outside.

## Elements documentation:

Technical documentation is currently not automatically generated. You need to heads to the class declaration to know the arguments and dependencies of your element. You can easily spot the elements' classes, they all begin by a `X`.

## When your config is right

After you tested your config locally, don't forget to commit and push !

On startup, CKli pull all the stacks repositories, so everyone will get updated automatically.
