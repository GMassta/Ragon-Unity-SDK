using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using NetStack.Serialization;
using Ragon.Common;
using UnityEditor;
using UnityEngine;

namespace Ragon.Client
{
  public class RagonRoom : IRagonRoom, IRoomInternal
  {
    private RagonConnection _connection;
    private IRagonNetworkListener _events;
    private BitBuffer _buffer = new BitBuffer(8192);
    private RagonSerializer _serializer = new RagonSerializer(8192);
    private List<RagonPlayer> _players = new List<RagonPlayer>();
    private Dictionary<string, RagonPlayer> _playersMap = new();
    private Dictionary<uint, RagonPlayer> _connections = new();
    private string _ownerId;
    private string _localId;
    
    public RagonRoom(IRagonNetworkListener events, RagonConnection connection, string id, string ownerId, string localPlayerId, int min, int max)
    {
      _events = events;
      _connection = connection;
      _ownerId = ownerId;
      _localId = localPlayerId;
      
      Id = id;
      MinPlayers = min;
      MaxPlayers = max;
    }

    public RagonPlayer Owner { get; private set; }
    public RagonPlayer LocalPlayer { get; private set; }
    
    public ReadOnlyCollection<RagonPlayer> Players => _players.AsReadOnly();
    public IReadOnlyDictionary<uint, RagonPlayer> Connections => _connections;
    public IReadOnlyDictionary<string, RagonPlayer> PlayersMap => _playersMap;
    
    public string Id { get; private set; }
    public int MinPlayers { get; private set; }
    public int MaxPlayers { get; private set; }

    public void AddPlayer(uint peerId, string playerId, string playerName)
    {
      var isOwner = playerId == _ownerId;
      var isLocal = playerId == _localId;
      
      var player = new RagonPlayer(peerId, playerId, playerName, isOwner, isLocal);
      
      if (player.IsLocal)
        LocalPlayer = player;

      if (player.IsOwner)
        Owner = player;
        
      _players.Add(player);
      _playersMap.Add(player.Id, player);
      _connections.Add(player.PeerId, player);
    }

    public void RemovePlayer(string playerId)
    {
      _playersMap.Remove(playerId, out var player);
      _players.Remove(player);
      _connections.Remove(player.PeerId);
    }

    public void OnOwnershipChanged(string playerId)
    {
      foreach (var player in _players)
      {
        if (player.Id == playerId)
          Owner = player;
        player.IsOwner = player.Id == playerId;
      }
    }

    public void LoadScene(string map)
    {
      if (!LocalPlayer.IsOwner)
      {
        Debug.LogWarning("Only owner can change map");
        return;
      }

      var mapRaw = Encoding.UTF8.GetBytes(map).AsSpan();
      var sendData = new byte[mapRaw.Length + 1];
      sendData[0] = (byte) RagonOperation.LOAD_SCENE;
      
      var data = sendData.AsSpan();
      var mapData = data.Slice(1, data.Length - 1);
      mapRaw.CopyTo(mapData);
      
      _connection.SendData(sendData);
    }

    public void SceneLoaded()
    {
      var sendData = new byte[] { (byte) RagonOperation.SCENE_IS_LOADED };
      _connection.SendData(sendData);
    }

    
    public void CreateEntity(ushort entityType, IRagonSerializable spawnPayload, RagonAuthority state = RagonAuthority.OWNER_ONLY,
      RagonAuthority events = RagonAuthority.OWNER_ONLY)
    {
      _buffer.Clear();
      spawnPayload.Serialize(_buffer);
      
      _serializer.Clear();
      _serializer.WriteOperation(RagonOperation.CREATE_ENTITY);
      _serializer.WriteUShort(entityType);
      _serializer.WriteByte((byte) state);
      _serializer.WriteByte((byte) events);
      
      if (_buffer.Length > 0)
      {
        Span<byte> payloadData = _serializer.GetWritableData(_buffer.Length);
        _buffer.ToSpan(ref payloadData);
      }

      var sendData = _serializer.ToArray();
      _connection.SendData(sendData);
    }

    public void DestroyEntity(int entityId, IRagonSerializable destroyPayload)
    {
      _buffer.Clear();
      destroyPayload.Serialize(_buffer);
      
      _serializer.Clear();
      _serializer.WriteOperation(RagonOperation.DESTROY_ENTITY);
      _serializer.WriteInt(entityId);
      
      if (_buffer.Length > 0)
      {
        Span<byte> payloadData = _serializer.GetWritableData(_buffer.Length);
        _buffer.ToSpan(ref payloadData);
      }

      var sendData = _serializer.ToArray();
      _connection.SendData(sendData);
    }

    public void SendEntityEvent(ushort evntCode, int entityId, RagonExecutionMode eventMode = RagonExecutionMode.SERVER_ONLY)
    {
      if (eventMode == RagonExecutionMode.LOCAL_ONLY)
      {
        _buffer.Clear();
        _events.OnEntityEvent(entityId, evntCode, _buffer);
        return;
      }
      
      _serializer.Clear();
      _serializer.WriteOperation(RagonOperation.REPLICATE_ENTITY_EVENT);
      _serializer.WriteUShort(evntCode);
      _serializer.WriteInt(entityId);
      
      if (eventMode == RagonExecutionMode.LOCAL_AND_SERVER)
      {
        _buffer.Clear();
        _events.OnEntityEvent(entityId, evntCode, _buffer);
      }

      var sendData = _serializer.ToArray();
      _connection.SendData(sendData);
    }

    public void SendEvent(ushort evntCode, IRagonSerializable payload, RagonExecutionMode eventMode = RagonExecutionMode.SERVER_ONLY)
    {
      _buffer.Clear();
      payload.Serialize(_buffer);
      
      if (eventMode == RagonExecutionMode.LOCAL_ONLY)
      {
        _events.OnEvent(evntCode, _buffer);
        return;
      }
      
      _serializer.Clear();
      _serializer.WriteOperation(RagonOperation.REPLICATE_EVENT);
      _serializer.WriteUShort(evntCode);
      
      var eventData = _serializer.GetWritableData(_buffer.Length);
      _buffer.ToSpan(ref eventData);
      
      if (eventMode == RagonExecutionMode.LOCAL_AND_SERVER)
      {
        _events.OnEvent(evntCode, _buffer);
      }

      var sendData = _serializer.ToArray();
      _connection.SendData(sendData);
    }

    public void SendEntityEvent(ushort evntCode, int entityId, IRagonSerializable payload, RagonExecutionMode eventMode = RagonExecutionMode.SERVER_ONLY)
    {
      _buffer.Clear();
      payload.Serialize(_buffer);
      
      if (eventMode == RagonExecutionMode.LOCAL_ONLY)
      {
        _events.OnEntityEvent(entityId, evntCode, _buffer);
        return;
      }
      
      _serializer.Clear();
      _serializer.WriteOperation(RagonOperation.REPLICATE_ENTITY_EVENT);
      _serializer.WriteUShort(evntCode);
      _serializer.WriteInt(entityId);
      
      var eventPayload = _serializer.GetWritableData(_buffer.Length);
      _buffer.ToSpan(ref eventPayload);
      
      if (eventMode == RagonExecutionMode.LOCAL_AND_SERVER)
      {
        _events.OnEntityEvent(entityId, evntCode, _buffer);
      }

      var sendData = _serializer.ToArray();
      _connection.SendData(sendData);
    }

    public void SendEvent(ushort evntCode, RagonExecutionMode eventMode = RagonExecutionMode.SERVER_ONLY)
    {
      if (eventMode == RagonExecutionMode.LOCAL_ONLY)
      {
        _buffer.Clear();
        _events.OnEvent(evntCode, _buffer);
        return;
      }
      
      _serializer.Clear();
      _serializer.WriteOperation(RagonOperation.REPLICATE_EVENT);
      _serializer.WriteUShort(evntCode);
      
      if (eventMode == RagonExecutionMode.LOCAL_AND_SERVER)
      {
        _buffer.Clear();
        _events.OnEvent(evntCode, _buffer);
      }

      var sendData = _serializer.ToArray();
      _connection.SendData(sendData);
    }

    public void SendEntityState(int entityId, IRagonSerializable payload)
    {
      _buffer.Clear();
      payload.Serialize(_buffer);
      
      _serializer.Clear();
      _serializer.WriteOperation(RagonOperation.REPLICATE_ENTITY_STATE);
      _serializer.WriteInt(entityId);
      
      var entityData = _serializer.GetWritableData(_buffer.Length);
      
      _buffer.ToSpan(ref entityData);

      var sendData = _serializer.ToArray();
      _connection.SendData(sendData);
    }
  }
}