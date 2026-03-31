using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace PcmHacking;

/// <summary>
/// A circular buffer implementation.
/// </summary>
/// <remarks>
/// Copied from https://github.com/JacobFreck/CircularBuff/blob/main/src/CircularBuff/CircularBuffer.cs
/// </remarks>
/// <typeparam name="T"></typeparam>
public class CircularBuffer<T> : IEnumerable, IEnumerable<T>
{
    internal T[] _arr;
    internal int _start;
    internal int _size;

    public CircularBuffer(int size)
    {
        if (size <= 0)
            throw new ArgumentException($"Invalid argument {nameof(size)} with value {size}");

        _arr = new T[size];
        _start = 0;
        _size = 0;
    }

    public int Count => _size;
    internal int Start => _start;
    internal T[] Arr => _arr;

    public void Add(T item)
    {
        int nextIndex = (_start + _size) % _arr.Length;

        if (_size == _arr.Length)
            _start = (_start + 1) % _arr.Length;

        _size = Math.Min(_size + 1, _arr.Length);
        _arr[nextIndex] = item;
    }

    public void Clear()
    {
        Array.Clear(_arr, 0, _arr.Length);
        _start = 0;
        _size = 0;
    }

    public bool Contains(T item)
    {
        for (int i = _start; i < _start + _size; i++)
        {
            int nextIndex = (_start + _size) % _arr.Length;
            T other = _arr[nextIndex];
            if (other is not null && other.Equals(item))
                return true;
        }

        return false;
    }


    public void CopyTo(T[] array, int arrayIndex)
    {
        _ = array ?? throw new ArgumentNullException(nameof(array));

        if ((array != null) && (array.Rank != 1))
        {
            throw new ArgumentException("Multi-dimensional arrays not supported");
        }

        try
        {
            Array.Copy(_arr, 0, array!, arrayIndex, _size);
        }
        catch (ArrayTypeMismatchException ex)
        {
            throw new ArgumentException("Array type mismatch {exception}", ex);
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = _start; i < _start + _size; i++)
            yield return _arr[i % _arr.Length];
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public bool Remove()
    {
        if (_size == 0)
            return false;

        _size--;

        return true;
    }
}