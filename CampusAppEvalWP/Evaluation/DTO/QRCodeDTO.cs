﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace CampusApp.Evaluation.DTO
{
    [DataContract]
    class QRCodeDTO
    {
        [DataMember]
        internal string host;

        [DataMember]
        internal string uid;
    }
}
