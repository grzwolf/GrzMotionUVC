using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Loupe
{
    public partial class Loupe: Form
    {
        public Loupe()
        {
            InitializeComponent();

            magnifyingGlass1.UpdateTimer.Start();
        }
    }
}
