#define CLIENT_STATE_STORER__PHYSICS
#define CLIENT_STATE_STORER__PHYSICS_2D
#define CLIENT_STATE_STORER__SPAWN_AFTER_SNAPSHOT

using Fusion;
using System;
using System.Collections.Generic;
using UnityEngine;
using static Fusion.NetworkRigidbodyBase;

namespace PhotonFusionUtil
{
    public class ClientStateStorer
    {
        private int _tick;

        private Dictionary<NetworkId, NetworkPrefabId> _prefabDict;
        private Dictionary<NetworkId, (Vector3 pos, Rotation rot)> _transformDict;
        private Dictionary<NetworkId, (
            Vector3 velocity, Vector3 angularVelocity,
            float mass, float drag, float aDrag,
            NetworkRigidbodyFlags flags, int constraints)> _rbDict;

        private Dictionary<object, Lazy<object>> _storeEventDict = new();
        public Dictionary<object, object> _storeDict = new();

        private Dictionary<NetworkId, NetworkId> _updatedNetworkIdDict;

        public void Add(object uniqueId, Lazy<object> store)
        {
            _storeEventDict.Add(uniqueId, store);
        }

        public void TryRestore<T>(object uniqueId, Action<T> restore)
        {
            if (_storeDict.ContainsKey(uniqueId)) restore?.Invoke((T)_storeDict[uniqueId]);
        }

        public void CheckUpdateNetworkId(NetworkId networkId, Action<NetworkId> updated)
        {
            if (_updatedNetworkIdDict.ContainsKey(networkId)) updated?.Invoke(_updatedNetworkIdDict[networkId]);
        }

        /// <summary>
        /// Run just before Shutdown() the old Runner.
        /// Stores information about NetworkObjects generated by Spawn().
        /// Store Position, Rotation, Velocity, AngularVelocity by default.
        /// </summary>
        public void Store(NetworkRunner runner, Func<NetworkObject, bool> storeConditions = null)
        {
            storeConditions ??= (no) => true;
            _tick = runner.Tick;

            var netBehaviours = runner.GetAllBehaviours<NetworkBehaviour>();

            var capacity = netBehaviours.Count / 2;
            _prefabDict = new(capacity);
            _transformDict = new(capacity);
            _rbDict = new(capacity);

            foreach (var NB in netBehaviours)
            {
                if (_prefabDict.ContainsKey(NB.Object.Id)) continue;
                if (storeConditions.Invoke(NB.Object) && runner.Config.PrefabTable.TryGetId(NB.Object.NetworkGuid, out var prefabId))
                {
                    var netId = NB.Object.Id;
                    var position = Vector3.zero;
                    var rotation = Quaternion.identity;

#if CLIENT_STATE_STORER__PHYSICS
                    if (NB.Object.TryGetBehaviour<NetworkRigidbody>(out var rb))
                    {
                        rb.ReadNetworkRigidbodyFlags(out var flags, out var constraints);
                        _rbDict.Add(netId, (rb.ReadVelocity(), rb.ReadAngularVelocity(),
                            rb.ReadMass(), rb.ReadDrag(), rb.ReadAngularDrag(), flags, (int)constraints));
                    }
#endif
#if CLIENT_STATE_STORER__PHYSICS_2D
                    if (NB.Object.TryGetBehaviour<NetworkRigidbody2D>(out var rb2d))
                    {
                        rb2d.ReadNetworkRigidbodyFlags(out var flags, out var constraints);
                        _rbDict.Add(netId, (rb2d.ReadVelocity(), Vector3.forward * rb2d.ReadAngularVelocity(),
                            rb2d.ReadMass(), rb2d.ReadDrag(), rb2d.ReadAngularDrag(), flags, (int)constraints));
                    }
#endif
                    if (NB.Object.TryGetBehaviour<NetworkPositionRotation>(out var posRot))
                    {
                        position = posRot.ReadPosition();
                        rotation = posRot.ReadRotation();
                    }
                    else if (NB.Object.TryGetBehaviour<NetworkPosition>(out var pos))
                    {
                        position = pos.ReadPosition();
                        rotation = Quaternion.identity;
                    }

                    _transformDict.Add(netId, (position, rotation));
                    _prefabDict.Add(netId, prefabId);
                }
            }

            _storeDict = new(_storeEventDict.Count);
            foreach (var pair in _storeEventDict) _storeDict.Add(pair.Key, pair.Value.Value);
            _storeEventDict = null;
        }

        /// <summary>
        /// Respawn the NetworkObjects based on the stored state.
        /// Can be executed only once per storing.
        /// </summary>
        public void SpawnsAndRestores(NetworkRunner runner,
            Action<NetworkRunner, NetworkObject> onBeforeSpawned = null,
            Action<NetworkRunner, NetworkObject> onAfterSpawned = null)
        {
            // Advance ResumeSnapshot's Tick to old Runner's Tick
            while (runner.Tick != _tick) runner.Simulation.Update(runner.DeltaTime);

            foreach (var no in runner.GetResumeSnapshotNetworkObjects())
            {
                if (!_prefabDict.ContainsKey(no.Id)) continue;
                _prefabDict.Remove(no.Id);

                var networkObject = runner.Spawn(no, _transformDict[no.Id].pos, _transformDict[no.Id].rot,
                    onBeforeSpawned: (runner, NO) =>
                    {
                        OnBeforeSpawnedBase(NO.Id, NO);
                        onBeforeSpawned?.Invoke(runner, NO);
                    });
                onAfterSpawned?.Invoke(runner, no);
            }

#if CLIENT_STATE_STORER__SPAWN_AFTER_SNAPSHOT
            _updatedNetworkIdDict = new(_prefabDict.Count);

            // Spawns objects created after the PushHostMigrationSnapshot(). Note that the NetworkId will change
            foreach (var pair in _prefabDict)
            {
                var oldNetId = pair.Key;
                var prefabId = pair.Value;

                var networkObject = runner.Spawn(prefabId, _transformDict[oldNetId].pos, _transformDict[oldNetId].rot,
                    onBeforeSpawned: (runner, NO) =>
                    {
                        OnBeforeSpawnedBase(oldNetId, NO);
                        _updatedNetworkIdDict.Add(oldNetId, NO.Id);

                        onBeforeSpawned?.Invoke(runner, NO);
                    });
                onAfterSpawned?.Invoke(runner, networkObject);
            }
#endif
        }
        private void OnBeforeSpawnedBase(NetworkId netId, NetworkObject no)
        {
#if CLIENT_STATE_STORER__PHYSICS
            if (no.TryGetBehaviour<NetworkRigidbody>(out var rb))
            {
                rb.WriteVelocity(_rbDict[netId].velocity);
                rb.WriteAngularVelocity(_rbDict[netId].angularVelocity);
                rb.WriteNetworkRigidbodyFlags(_rbDict[netId].flags, (RigidbodyConstraints)_rbDict[netId].constraints);
                rb.WriteMass(_rbDict[netId].mass);
                rb.WriteDrag(_rbDict[netId].drag);
                rb.WriteAngularDrag(_rbDict[netId].aDrag);
            }
#endif
#if CLIENT_STATE_STORER__PHYSICS_2D
            else if (no.TryGetBehaviour<NetworkRigidbody2D>(out var rb2d))
            {
                rb2d.WriteVelocity(_rbDict[netId].velocity);
                rb2d.WriteAngularVelocity(_rbDict[netId].angularVelocity.z);
                rb2d.WriteNetworkRigidbodyFlags(_rbDict[netId].flags, (RigidbodyConstraints2D)_rbDict[netId].constraints);
                rb2d.WriteMass(_rbDict[netId].mass);
                rb2d.WriteDrag(_rbDict[netId].drag);
                rb2d.WriteAngularDrag(_rbDict[netId].aDrag);
            }
#endif
        }
    }

    public static class ClientStatesStorerUtil
    {
        private static Dictionary<NetworkRunner, NetworkRunner> oldRunnerDict = new();
        private static Dictionary<NetworkRunner, ClientStateStorer> storerDict = new();

        private static ClientStateStorer Storer(this NetworkRunner runner)
        {
            if (!storerDict.ContainsKey(runner))
            {
                var storer = new ClientStateStorer();
                storerDict.Add(runner, storer);
            }
            return storerDict[runner];
        }
        private static NetworkRunner OldRunner(this NetworkRunner runner)
            => oldRunnerDict.ContainsKey(runner) ? oldRunnerDict[runner] : null;

        /// <summary>
        /// Add at the beginning of OnHostMigration()
        /// </summary>
        public static void Store(this NetworkRunner runner, Func<NetworkObject, bool> storeConditions = null)
        {
            runner.Storer().Store(runner, storeConditions);
        }

        /// <summary>
        /// Add it right after spawning a new Runner in OnHostMigration()
        /// </summary>
        public static void SetOldRunner(this NetworkRunner runner, NetworkRunner oldRunner) => oldRunnerDict.Add(runner, oldRunner);

        /// <summary>
        /// Add at the beginning of HostMigrationResume()
        /// </summary>
        public static void SpawnsAndRestores(this NetworkRunner runner,
            Action<NetworkRunner, NetworkObject> onBeforeSpawned = null, Action<NetworkRunner, NetworkObject> onAfterSpawned = null)
        {
            runner.OldRunner().Storer().SpawnsAndRestores(runner, onBeforeSpawned, onAfterSpawned);
        }

        /// <summary>
        /// Networked properties and local variables can be stored and restored. Use with Spawned()
        /// </summary>
        public static void StoreAndTryRestore<T>(this NetworkRunner runner, object uniqueId, Lazy<object> store, Action<T> restore)
        {
            runner.Storer().Add(uniqueId, store);
            if (oldRunnerDict.ContainsKey(runner))
            {
                runner.OldRunner().Storer().TryRestore<T>(uniqueId, receiveData => restore?.Invoke(receiveData));
            }
        }

#if CLIENT_STATE_STORER__SPAWN_AFTER_SNAPSHOT
        /// <summary>
        /// Can handle NetworkId changes. Use with Spawned()
        /// </summary>
        public static void ReceiveUpdatedNetworkId(
            this NetworkRunner runner,
            Action<(NetworkId oldId, NetworkId newId)> updated,
            params NetworkId[] networkIds)
        {
            if (!oldRunnerDict.ContainsKey(runner)) return;
            foreach (var id in networkIds)
            {
                runner.OldRunner().Storer().CheckUpdateNetworkId(id, newId => updated?.Invoke((id, newId)));
            }
        }
#endif

        /// <summary>
        /// Add at the end of OnPlayerJoined()
        /// </summary>
        public static void RemoveOldRunnerAndStorer(this NetworkRunner runner)
        {
            if (runner.OldRunner() != null && storerDict.ContainsKey(runner.OldRunner()))
            {
                storerDict.Remove(runner.OldRunner());
                oldRunnerDict.Remove(runner);
            }
        }
    }
}
