I migrate api Rocket and other NuGet packages to dnxcore that they be used on linux / mac / windows ( xboxone ... ) . 

And I also NecroBot migrated to a POC to test the API. That works very well ;-).


________________________________________________
Before for install .net core sdk in your os >>
.NET - Powerful Open Source Development

git clone GitHub - cormaltes/NecroBot.CLI.NetCore
cd NecroBot.CLI.NetCore
dotnet restore
cd PoGo.NecroBot.CLI.NetCore
dotnet run


Edit config file auth.json into PoGo.NecroBot.CLI.NetCore/Congig


run again with only

dotnet run (in dir /NecroBot.CLI.NetCore/PoGo.NecroBot.CLI.NetCore)
________________________________________________________________

If you update by new version of git, you must execute again : dotnet restore ( for update packages if there are changed).


The principal source code are from :
- Insensitivity : PogoProtos
- ForexRev : Pokemon-Go-Rocket-API
- Necrobot : NECRBOTIO / NoxxDev