using System;
using System.Collections;

namespace CST.Collections
{
	/// <summary>
	/// Summary description for Set.
	/// </summary>
	public class Set
	{
		public Set()
		{
			Init();
		}

		public Set(ICollection collection)
		{
			Init();

			foreach (object o in collection)
			{
				Add(o);
			}
		}

		public virtual object this[object key] 
		{
			get
			{
				return hash[key];
			}
		}

		private void Init()
		{
			hash = new Hashtable();
		}

		public void Add(object o)
		{
			if (hash.ContainsKey(o) == false)
				hash.Add(o, o);
		}

		public void AddAll(Set s)
		{
			foreach (object o in s)
			{
				Add(o);
			}
		}

        public void AddAll(ICollection coll)
        {
            foreach (object o in coll)
            {
                Add(o);
            }
        }

		public void Remove(object o)
		{
			hash.Remove(o);
		}

		public bool Contains(object o)
		{
			if (hash.Contains(o))
				return true;
			else
				return false;
		}

		public void Clear()
		{
			Init();
		}

		public IEnumerator GetEnumerator()
		{
			return hash.Keys.GetEnumerator();
		}

		public bool IsEmpty()
		{
			if (hash.Count == 0)
				return true;
			else
				return false;
		}

		public int Count
		{
            get
            {
                return hash.Count;
            }
		}


		private Hashtable hash;
	}
}
