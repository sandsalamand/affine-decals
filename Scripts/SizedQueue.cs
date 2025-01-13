using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class SizedQueue<T> : Queue<T>
{
	public int FixedCapacity { get; }
	public SizedQueue(int fixedCapacity)
	{
		this.FixedCapacity = fixedCapacity;
	}

	public SizedQueue(int fixedCapacity, IEnumerable<T> array)
	{
		this.FixedCapacity = fixedCapacity;
		if (array == null)
			return;

		foreach (T element in array)
			this.Enqueue(element);
	}

	public SizedQueue(IEnumerable<T> array)
	{
		if (array == null)
			return;

		this.FixedCapacity = array.Count();

		foreach (T element in array)
			this.Enqueue(element);
	}

	/// <summary>
	/// If the total number of items exceed the capacity, the oldest ones automatically dequeues.
	/// </summary>
	/// <returns>The dequeued value, or default if nothing was dequeued.</returns>
	public new void Enqueue(T item)
	{
		if (base.Count > 0 && base.Count == FixedCapacity)
			base.Dequeue();

		base.Enqueue(item);
	}

	/// <summary>	 </summary>
	/// <param name="newCapacity">This needs to be >= the sum of the queues' fixed capacity</param>
	/// <returns>The new SizedQueue, or null if it failed</returns>
	public static SizedQueue<T> Concatenate(SizedQueue<T> firstQueue, SizedQueue<T> secondQueue, int newCapacity)
	{
		if (newCapacity < firstQueue.FixedCapacity + secondQueue.FixedCapacity)
		{
			Debug.LogError("Tried to concatenate SizedQueues with insufficient specified capacity in the new queue");
			return null;
		}

		SizedQueue<T> newQueue = new(newCapacity);
		foreach (T element in firstQueue)
			newQueue.Enqueue(element);

		foreach (T element in secondQueue)
			newQueue.Enqueue(element);

		return newQueue;
	}

	//Necessary for passing to the shader with the fixed capacity
	//Optimization opportunity: This is inefficient because we don't have access to the base Queue's backing array. Could write a custom Queue implementation.
	public T[] ToFixedArray(T emptyValue = default(T))
	{
		T[] returnArray = new T[FixedCapacity];
		System.Array.Fill(returnArray, emptyValue);

		int i = 0;
		foreach (var element in this)
		{
			returnArray[i] = element;
			i++;
		}
		return returnArray;
	}
}
