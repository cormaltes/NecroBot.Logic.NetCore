#region using directives

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GeoCoordinatePortable;
using PoGo.NecroBot.Logic.Common;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Utils;
using PokemonGo.RocketAPI.Extensions;
using POGOProtos.Map.Fort;

#endregion

namespace PoGo.NecroBot.Logic.Tasks
{
    public static class FarmPokestopsGpxTask
    {
        private static int curTrkSeg;
        private static int curTrkPt;
        private static DateTime _lastTasksCall = DateTime.Now;

        public static async Task Execute(ISession session, CancellationToken cancellationToken)
        {
            var tracks = GetGpxTracks(session);
            var eggWalker = new EggWalker(1000, session);

            for (var curTrk = 0; curTrk < tracks.Count; curTrk++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var track = tracks.ElementAt(curTrk);
                var trackSegments = track.Segments;

                if (curTrkSeg >= trackSegments.Count) curTrkSeg = 0;
                for (; curTrkSeg < trackSegments.Count; curTrkSeg++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var trackPoints = track.Segments.ElementAt(curTrkSeg).TrackPoints;

                    if (curTrkPt >= trackPoints.Count) curTrkPt = 0;
                    for (; curTrkPt < trackPoints.Count; curTrkPt++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var nextPoint = trackPoints.ElementAt(curTrkPt);
                        var distance = LocationUtils.CalculateDistanceInMeters(session.Client.CurrentLatitude,
                            session.Client.CurrentLongitude,
                            Convert.ToDouble(nextPoint.Lat, CultureInfo.InvariantCulture),
                            Convert.ToDouble(nextPoint.Lon, CultureInfo.InvariantCulture));

                        if (distance > 5000)
                        {
                            session.EventDispatcher.Send(new ErrorEvent
                            {
                                Message =
                                    session.Translation.GetTranslation(TranslationString.DesiredDestTooFar,
                                        nextPoint.Lat, nextPoint.Lon, session.Client.CurrentLatitude,
                                        session.Client.CurrentLongitude)
                            });
                            break;
                        }

                        var pokestopList = await GetPokeStops(session);
                        
                        while (pokestopList.Any())
                        // warning: this is never entered due to ps cooldowns from UseNearbyPokestopsTask 
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            pokestopList =
                                pokestopList.OrderBy(
                                    i =>
                                        LocationUtils.CalculateDistanceInMeters(session.Client.CurrentLatitude,
                                            session.Client.CurrentLongitude, i.Latitude, i.Longitude)).ToList();
                            var pokeStop = pokestopList[0];
                            pokestopList.RemoveAt(0);

                            var fortInfo =
                                await session.Client.Fort.GetFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);

                            if (pokeStop.LureInfo != null)
                            {
                                await CatchLurePokemonsTask.Execute(session, pokeStop, cancellationToken);
                            }

                            var fortSearch =
                                await session.Client.Fort.SearchFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);

                            if (fortSearch.ExperienceAwarded > 0)
                            {
                                session.EventDispatcher.Send(new FortUsedEvent
                                {
                                    Id = pokeStop.Id,
                                    Name = fortInfo.Name,
                                    Exp = fortSearch.ExperienceAwarded,
                                    Gems = fortSearch.GemsAwarded,
                                    Items = StringUtils.GetSummedFriendlyNameOfItemAwardList(fortSearch.ItemsAwarded),
                                    Latitude = pokeStop.Latitude,
                                    Longitude = pokeStop.Longitude
                                });
                            }
                            else
                            {
                                await RecycleItemsTask.Execute(session, cancellationToken);
                            }

                            if (fortSearch.ItemsAwarded.Count > 0)
                            {
                                await session.Inventory.RefreshCachedInventory();
                            }
                        }

                        if (DateTime.Now > _lastTasksCall)
                        {
                            _lastTasksCall =
                                DateTime.Now.AddMilliseconds(Math.Min(session.LogicSettings.DelayBetweenPlayerActions,
                                    3000));

                            await RecycleItemsTask.Execute(session, cancellationToken);

                            if (session.LogicSettings.EvolveAllPokemonWithEnoughCandy ||
                                session.LogicSettings.EvolveAllPokemonAboveIv ||
                                session.LogicSettings.UseLuckyEggsWhileEvolving ||
                                session.LogicSettings.KeepPokemonsThatCanEvolve)
                            {
                                await EvolvePokemonTask.Execute(session, cancellationToken);
                            }
                            await GetPokeDexCount.Execute(session, cancellationToken);

                            if (session.LogicSettings.AutomaticallyLevelUpPokemon)
                            {
                                await LevelUpPokemonTask.Execute(session, cancellationToken);
                            }
                            if (session.LogicSettings.UseLuckyEggConstantly)
                            {
                                await UseLuckyEggConstantlyTask.Execute(session, cancellationToken);
                            }
                            if (session.LogicSettings.UseIncenseConstantly)
                            {
                                await UseIncenseConstantlyTask.Execute(session, cancellationToken);
                            }
                            if (session.LogicSettings.TransferDuplicatePokemon)
                            {
                                await TransferDuplicatePokemonTask.Execute(session, cancellationToken);
                            }
                            if (session.LogicSettings.TransferWeakPokemon)
                            {
                                await TransferWeakPokemonTask.Execute(session, cancellationToken);
                            }
                            if (session.LogicSettings.RenamePokemon)
                            {
                                await RenamePokemonTask.Execute(session, cancellationToken);
                            }

                            if (session.LogicSettings.AutoFavoritePokemon)
                            {
                                await FavoritePokemonTask.Execute(session, cancellationToken);
                            }

                            if (session.LogicSettings.SnipeAtPokestops || session.LogicSettings.UseSnipeLocationServer)
                            {
                                await SnipePokemonTask.Execute(session, cancellationToken);
                            }
                        }

                        var geo = new GeoCoordinate(Convert.ToDouble(trackPoints.ElementAt(curTrkPt).Lat, CultureInfo.InvariantCulture),
                            Convert.ToDouble(trackPoints.ElementAt(curTrkPt).Lon, CultureInfo.InvariantCulture));

                        await session.Navigation.Move(geo,
                            async () =>
                            {
                                await CatchNearbyPokemonsTask.Execute(session, cancellationToken);
                                //Catch Incense Pokemon
                                await CatchIncensePokemonsTask.Execute(session, cancellationToken);
                                await UseNearbyPokestopsTask.Execute(session, cancellationToken);
                                return true;
                            },
                            session,
                            cancellationToken);

                        await eggWalker.ApplyDistance(distance, cancellationToken);
                    } //end trkpts
                } //end trksegs
            } //end tracks
        }

        private static List<GpxReader.Trk> GetGpxTracks(ISession session)
        {
            var xmlString = File.ReadAllText(session.LogicSettings.GpxFile);
            var readgpx = new GpxReader(xmlString, session);
            return readgpx.Tracks;
        }

        //Please do not change GetPokeStops() in this file, it's specifically set
        //to only find stops within 40 meters
        //this is for gpx pathing, we are not going to the pokestops,
        //so do not make it more than 40 because it will never get close to those stops.
        private static async Task<List<FortData>> GetPokeStops(ISession session)
        {
            List<FortData> pokeStops = await UpdateFortsData(session);
            session.EventDispatcher.Send(new PokeStopListEvent { Forts = pokeStops });

            // Wasn't sure how to make this pretty. Edit as needed.
            pokeStops = pokeStops.Where(
                    i =>
                        i.Type == FortType.Checkpoint &&
                        i.CooldownCompleteTimestampMs < DateTime.UtcNow.ToUnixTime() &&
                        ( // Make sure PokeStop is within 40 meters or else it is pointless to hit it
                            LocationUtils.CalculateDistanceInMeters(
                                session.Client.CurrentLatitude, session.Client.CurrentLongitude,
                                i.Latitude, i.Longitude) < 40) ||
                        session.LogicSettings.MaxTravelDistanceInMeters == 0
                ).ToList();

            return pokeStops.ToList();
        }

        private static async Task<List<FortData>> UpdateFortsData(ISession session)
        {
            var mapObjects = await session.Client.Map.GetMapObjects();
            
            var pokeStops = mapObjects.Item1.MapCells.SelectMany(i => i.Forts)
                .Where(
                    i =>
                        i.Type == FortType.Checkpoint &&
                        i.CooldownCompleteTimestampMs < DateTime.UtcNow.ToUnixTime() &&
                        (
                            LocationUtils.CalculateDistanceInMeters(
                                session.Client.CurrentLatitude, session.Client.CurrentLongitude,
                                i.Latitude, i.Longitude) < session.LogicSettings.MaxTravelDistanceInMeters) ||
                        session.LogicSettings.MaxTravelDistanceInMeters == 0
                );

            return pokeStops.ToList();
        }
    }
}