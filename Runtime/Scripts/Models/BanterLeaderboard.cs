
using System;

namespace Banter.SDK
{
    public class Score{
        public string id;
        public string name;
        public float score;
    }

    [Serializable]
    public class Board{
        public Score[] scores;
        public string sort;
    }


    [Serializable]
    public class UpdateScores{
        public string board;
        public Board scores;
    }
}