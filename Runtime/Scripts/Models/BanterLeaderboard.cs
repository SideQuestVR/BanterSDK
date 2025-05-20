
using System;

namespace Banter.SDK
{
    [Serializable]
    public class Score{
        public string id;
        public string name;
        public float score;
        public string color;
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