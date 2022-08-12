using System;
using System.Collections.Generic;
using Ragon.Common;
using UnityEngine;

namespace Ragon.Client.Prototyping
{
  [DefaultExecutionOrder(-1400)]
  public class RagonEntity<TState> :
    MonoBehaviour,
    IRagonEntity,
    IRagonEntityInternal
    where TState : IRagonState, new()
  {
    private delegate void OnEventDelegate(RagonPlayer player, RagonSerializer buffer);

    public bool AutoReplication => _replication;
    public bool IsAttached => _attached;
    public bool IsMine => _mine;
    public int Id => _entityId;
    public RagonPlayer Owner => _owner;

    [SerializeField, ReadOnly] private int _entityType;
    [SerializeField, ReadOnly] private int _entityId;
    [SerializeField, ReadOnly] private bool _mine;
    [SerializeField, ReadOnly] private RagonPlayer _owner;
    [SerializeField, ReadOnly] private bool _attached;
    [SerializeField, ReadOnly] private bool _replication;

    protected RagonRoom Room;
    protected TState State;

    private byte[] _spawnPayload;
    private byte[] _destroyPayload;

    private Dictionary<int, OnEventDelegate> _events = new();

    public void Attach(int entityType, RagonPlayer owner, int entityId, byte[] payloadData)
    {
      _entityType = entityType;
      _entityId = entityId;
      _owner = owner;
      _attached = true;
      _mine = RagonNetwork.Room.LocalPlayer.Id == owner.Id;
      _spawnPayload = payloadData;
      _replication = true;

      State = new TState();

      OnCreatedEntity();
    }

    public void ChangeOwner(RagonPlayer newOwner)
    {
      _owner = newOwner;
      _mine = RagonNetwork.Room.LocalPlayer.Id == newOwner.Id;
    }

    public void Detach(byte[] payload)
    {
      _destroyPayload = payload;
      OnDestroyedEntity();
      Destroy(gameObject);
    }

    public void ProcessState(RagonSerializer data)
    {
      State.Deserialize(data);
    }

    public void ProcessEvent(RagonPlayer player, ushort eventCode, RagonSerializer data)
    {
      if (_events.ContainsKey(eventCode))
        _events[eventCode]?.Invoke(player, data);
    }

    public void OnEvent<TEvent>(Action<RagonPlayer, TEvent> callback) where TEvent : IRagonEvent, new()
    {
      var eventCode = RagonNetwork.EventManager.GetEventCode<TEvent>(new TEvent());
      if (_events.ContainsKey(eventCode))
      {
        Debug.LogWarning($"Event already {eventCode} subscribed");
        return;
      }

      var t = new TEvent();
      _events.Add(eventCode, (player, buffer) =>
      {
        t.Deserialize(buffer);
        callback.Invoke(player, t);
      });
    }

    internal T GetPayload<T>(byte[] data) where T : IRagonPayload, new()
    {
      if (data == null) return new T();
      if (data.Length == 0) return new T();

      var serializer = new RagonSerializer();
      serializer.FromArray(data);

      var payload = new T();
      payload.Deserialize(serializer);

      return payload;
    }

    public int Type { get; private set; }

    public T GetSpawnPayload<T>() where T : IRagonPayload, new()
    {
      return GetPayload<T>(_spawnPayload);
    }

    public T GetDestroyPayload<T>() where T : IRagonPayload, new()
    {
      return GetPayload<T>(_destroyPayload);
    }

    public void ReplicateEvent<TEvent>(
      TEvent evnt,
      RagonTarget target = RagonTarget.ALL,
      RagonReplicationMode replicationMode = RagonReplicationMode.SERVER_ONLY)
      where TEvent : IRagonEvent, new()
    {
      RagonNetwork.Room.ReplicateEntityEvent(evnt, _entityId, target, replicationMode);
    }

    public void ReplicateState()
    {
      RagonNetwork.Room.ReplicateEntityState(_entityId, State);
    }

    #region VIRTUAL

    public virtual void OnCreatedEntity()
    {
    }

    public virtual void OnDestroyedEntity()
    {
    }

    #endregion
  }
}