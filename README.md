# CK-Env

Invenietis/Signature Code multi-repository utilities.

Contains **CK.Env**, a series of libraries for repository orchestration, and **CKli**, its command line utility.

For Invenietis/Signature Code internal use only.

## A word about authentication

Private repositories (like `invenietis.visualstudio.com`) use the Git credentials stored on **Windows** by another Git tool (like Git for Windows).

You'll need to check out a private repository externally and save your credentials once before executing CKli, or authentication will fail because no credentials can be found on your machine.

## Using CKli

1. Create a root directory to contain all the CK-Env-cloned projects (eg. `C:\dev\CK-World`)
2. Clone this repository, `CK-Env`, inside the root directory
3. Build `CK-Env`
4. Execute CKli
5. Select your world

At this point, CKli will begin checking out all repositories from the world you have selected,
and you'll have access to the various available actions in this world.

## `develop` required

Currently, git repositories are checked out on the `develop` branch instead of their default one. Checkout will fail if repositories don't have a `develop` branch.

Please ensure all repositories in the various `*-World.xml` have a `develop` branch.

## Directory name conventions (when writing `*-World.xml`)

### Library projects

- `CK-AspNet-Projects`: Only for CK.AspNet.*
- `CK-Core-Projects`: Only for CK.Core, ActivityMonitor, Monitoring and below. Contains projects required by CK-Database and Signature build tools.
  - **Not for other CK-Projects or customer code.** Use `CK-Misc-Projects` or `*-Customer-Projects` for those.
  - Everything in `CK-Core-Projects` must be Spi-compliant.
  - Everything in `CK-Core-Projects` must appear in `CK-World.xml` and respect the many constraints in there.
  - Everything in `CK-Core-Projects` must be available on **public** repositories, and can be built using only **public** repositories.
- `CK-Database-Projects`: Only for CK-Database and related projects like CK.DB.*, CKSetup and CK-CodeGen.
  - **Not for customer DB code.** Use `*-Customer-Projects` for those.
  - Everything in `CK-Database-Projects` must be Spi-compliant.
  - Everything in `CK-Database-Projects` must appear in `CK-World.xml` and respect the many constraints in there.
- `Yodii-Projects`: Yodii and YodiiScript (note: only YodiiScript exists at this time).
- `SimpleGitVersion`: SGV and CodeCake projects. Spi-compliant.
  - Everything in `SimpleGitVersion` must exist at https://github.com/SimpleGitVersion/
- `CK-Misc-Projects`: Non-Spi-compliant library projects, starting with CK because that's how we do things.
  - Everything in `CK-Misc-Projects` must start with `CK`. ForSignature WCS/Library/SDK projects, see `Signature-Library-Projects`.
- `Signature-Library-Projects`: Non-Spi-compliant Signature-related library projects, used in customer projects or tools.
  - Signature libraries are related to logistics processes, servers and/or hardware like the Signature DPS libraries, the WCS server core, the SDK libraries and tools like CLIs and emulators.
  - **Not for customer code.** Use `Signature-Customer-Projects` for those.

### Customer projects

- `Signature-Customer-Projects`: Signature Logistics customer repositories for people like Domino's Pizza, Centravet, etc.
- `ENGIE-Customer-Projects`: ENGIE customer and library repositories with projects like Cofely (Feedermarket), Feedermarket.Client, and common libraries for ENGIE-specific GIS management and interop.

