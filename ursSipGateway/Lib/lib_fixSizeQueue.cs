using Project.Enums;
using Project.ProjectCtrl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace Project.Lib {

    public class FixedSizedQueue<T> {
        public ConcurrentQueue<T> Qu = new ConcurrentQueue<T>();

        private object lockObject = new object();
        private int _limit = 5;

        public FixedSizedQueue(int limit) {
            _limit = limit;
        }

        public void Clear() {
            lock (lockObject) {
                Qu.Clear();
            }
        }

        public void Enqueue(T obj) {
            Qu.Enqueue(obj);
            lock (lockObject) {
                T overflow;
                while (Qu.Count > _limit && Qu.TryDequeue(out overflow)) ;
            }
        }
    }


}
