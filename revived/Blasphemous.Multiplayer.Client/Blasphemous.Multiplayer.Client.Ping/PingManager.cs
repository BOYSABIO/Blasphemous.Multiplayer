using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Ping;

public class PingManager
{
	private readonly List<float> _delays = new List<float>(10);

	private float _currentInterval;

	private const int MAX_DELAYS = 10;

	private const float SEND_INTERVAL = 1f;

	public ushort AveragePing
	{
		get
		{
			if (_delays.Count == 0)
			{
				return 0;
			}
			return (ushort)(_delays.Sum() / (float)_delays.Count * 1000f);
		}
	}

	public void ReceivePing(float time)
	{
		float item = Time.time - time;
		_delays.Add(item);
		if (_delays.Count > 10)
		{
			_delays.RemoveAt(0);
		}
	}

	public void OnUpdate()
	{
		if (Main.Multiplayer.NetworkManager.IsConnected)
		{
			_currentInterval += Time.deltaTime;
			if (_currentInterval >= 1f)
			{
				_currentInterval = 0f;
				Main.Multiplayer.NetworkManager.SendPing(Time.time, AveragePing);
			}
		}
	}
}
