using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;

namespace X360
{
    /// <summary>
    /// Resource data
    /// </summary>
    public static class PublicResources
    {
        /// <summary>
        /// Dash Image
        /// </summary>
        public static Image DashImage { get { return CreatePlaceholderImage(); }}
        /// <summary>
        /// No Achievement image
        /// </summary>
        public static Image NoImage { get { return CreatePlaceholderImage(); }}
        /// <summary>
        /// Achievement locked image
        /// </summary>
        public static Image Locked { get { return CreatePlaceholderImage(); }}
        /// <summary>
        /// Paw icon
        /// </summary>
        public static Icon PawIcon { get { return SystemIcons.Application; }}
        /// <summary>
        /// GPL License
        /// </summary>
        public static string GPL { get { return global::X360.Properties.Resources.GPL30; }}

        private static Image CreatePlaceholderImage()
        {
            Bitmap image = new Bitmap(1, 1);
            image.SetPixel(0, 0, Color.Transparent);
            return image;
        }
    }
}
