/******************************************************************************
 * PastecAPI.cs
 * 
 * Copyright (c) 2019 Muziekweb
 * 
 * Author: C. Karreman
 * 
 * This code is a C Sharp implementation for accessing the web API of Visualink
 * Pastec. A Content Based Image Retreival system.
 * 
 * Pastec wensite:      http://pastec.io/
 * Pastec source code:  https://github.com/magwyz/pastec
 */

namespace PastecLib
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading.Tasks;

    [Serializable]
    public class PastecException : Exception
    {
        private static readonly Dictionary<string, string> errors = new Dictionary<string, string>
        {
            { "ERROR_GENERIC", "Generic error." },
            { "MISFORMATTED_REQUEST", "Misformatted request." },
            { "TOO_MANY_CLIENTS", "Too many clients connected to the server." },

            { "IMAGE_DATA_TOO_BIG", "Image data size too big." },
            { "IMAGE_NOT_INDEXED","Image not indexed." },
            { "IMAGE_NOT_DECODED", "The query image could not be decoded." },
            { "IMAGE_SIZE_TOO_SMALL", "Image size too small." },
            { "IMAGE_NOT_FOUND", "Image not found." },
            { "IMAGE_TAG_NOT_FOUND", "Image tag not found." },

            { "INDEX_NOT_FOUND", "Index not found." },
            { "INDEX_TAGS_NOT_FOUND", "Index tags not found." },
            { "INDEX_NOT_WRITTEN", "Index not written." },
            { "INDEX_TAGS_NOT_WRITTEN", "Index not written." },

            { "IMAGE_DOWNLOADER_HTTP_ERROR", "HTTP error when downloading an image." }
        };

        public PastecException(string type) : base(type != null && errors.ContainsKey(type) ? errors[type] : $"Undefined error ({type})") { }
    }

    [Serializable]
    public class SearchResult
    {
        public long ImageId;
        public string Tag;
        public double Score;
        public Rectangle BoundingRect;
    }

    public class PastecAPI
    {
        private readonly string host = "http://localhost:4212";

        public string Host => host;

        public PastecAPI() { }

        public PastecAPI(string host, int port, bool useSsl = false)
        {
            this.host = useSsl
                ? $"https://{host}:{port}"
                : $"http://{host}:{port}";
        }

        /// <summary>
        /// Executes the API call to the Pastec server using a JSON object. The 
        /// JSON object is added as request content.
        /// </summary>
        /// <param name="method">Request method</param>
        /// <param name="path">The API path</param>
        /// <param name="json">The JSON object with the attributes for the API call</param>
        /// <returns>Returns the JSON data object that is returnd by the server</returns>
        private async Task<dynamic> Request(HttpMethod method, string path, dynamic json)
        {
            using (HttpContent data = new StringContent(json.ToString(), Encoding.UTF8, "application/json"))
            {
                return await Request(method, path, data);
            }
        }

        /// <summary>
        /// Executes the API call to the Pastec server. The content can be null,
        /// text, JSON or JPEG data.
        /// </summary>
        /// <param name="method">Request method</param>
        /// <param name="path">The API path</param>
        /// <param name="data">The request content for the API call</param>
        /// <returns>Returns the JSON data object that is returnd by the server</returns>
        private async Task<dynamic> Request(HttpMethod method, string path, HttpContent data = null)
        {
            using (HttpClient client = new HttpClient())
            using (HttpRequestMessage request = new HttpRequestMessage(method, $"{Host}{path}")
            {
                Content = data
            })
            using (HttpResponseMessage res = await client.SendAsync(request))
            using (HttpContent content = res.Content)
            {
                return JsonConvert.DeserializeObject<dynamic>(await content.ReadAsStringAsync());
            }
        }

        /// <summary>
        /// Tests the result type of the API. Give the expected result type to 
        /// test. When the result type differs from the expected result, a 
        /// PastecException is thrown.
        /// </summary>
        /// <param name="expected">The correct result type for the executed call</param>
        /// <param name="result">The result data from the API call</param>
        /// <returns></returns>
        private bool TestResult(string expected, dynamic result)
        {
            if (expected.Equals((string)result["type"]))
            {
                return true;
            }
            else
            {
                throw new PastecException((string)result["type"]);
            }
        }

        /// <summary>
        /// Executes a Ping command to check is the server is online.
        /// </summary>
        /// <returns>Returns True when the server repied with the expected result. An error is thrown otherwise.</returns>
        public async Task<bool> Ping()
        {
            var json = JObject.FromObject(new
            {
                type = "PING"
            });

            var result = await Request(HttpMethod.Post, "/", json);

            return TestResult("PONG", result);
        }

        /// <summary>
        /// This call allows to add the signature of an image in the index to 
        /// make it available for searching. You need to provide the local 
        /// filepath to the image.
        /// </summary>
        /// <param name="imageId">Identifier for the image</param>
        /// <param name="filePath">Image filepath</param>
        /// <returns>Returns the image identifier, should be equal to the given imageId</returns>
        public async Task<long> IndexImageFile(long imageId, string filePath)
        {
            return await IndexImageData(imageId, await File.ReadAllBytesAsync(filePath));
        }

        /// <summary>
        /// This call allows to add the signature of an image in the index to 
        /// make it available for searching. You need to provide the compressed 
        /// binary data of the image and an id to identify it.
        /// </summary>
        /// <param name="imageId">Identifier for the image</param>
        /// <param name="imageData">The JPEG image data</param>
        /// <returns>Returns the image identifier, should be equal to the given imageId</returns>
        public async Task<long> IndexImageData(long imageId, byte[] imageData)
        {
            using (var data = new ByteArrayContent(imageData))
            {
                data.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");

                var result = await Request(HttpMethod.Put, $"/index/images/{imageId}", data);

                if (TestResult("IMAGE_ADDED", result))
                {
                    return (long)result["image_id"];
                }
            }

            return -1;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="imageId">Identifier for the image</param>
        /// <param name="imageUrl">Url to the image resource</param>
        /// <returns>Returns the image identifier, should be equal to the given imageId</returns>
        public async Task<long> IndexImageUrl(long imageId, string imageUrl)
        {
            var json = JObject.FromObject(new
            {
                url = imageUrl
            });

            var result = await Request(HttpMethod.Put, $"/index/images/{imageId}", json);

            if (TestResult("IMAGE_ADDED", result))
            {
                return (long)result["image_id"];
            }

            return -1;
        }

        /// <summary>
        /// This call removes the signature of an image in the index using its 
        /// id. Be careful to not call this method often if your index is  big 
        /// because it is currently very slow. 
        /// </summary>
        /// <param name="imageId"></param>
        /// <returns>Returns True when the tag is succesfully added.</returns>
        public async Task<long> RemoveImage(long imageId)
        {
            var result = await Request(HttpMethod.Delete, $"/index/images/{imageId}");

            if (TestResult("IMAGE_REMOVED", result))
            {
                return (long)result["image_id"];
            }

            return -1;
        }

        /// <summary>
        /// Add a tag to the image using the image identifier.
        /// </summary>
        /// <param name="imageId">Identifier for the image</param>
        /// <param name="tag">The tag for the image</param>
        /// <returns></returns>
        public async Task<bool> AddTag(long imageId, string tag)
        {
            using (HttpContent data = new StringContent(tag, Encoding.UTF8))
            {
                var result = await Request(HttpMethod.Put, $"/index/images/{imageId}/tag", data);

                return TestResult("IMAGE_TAG_ADDED", result);
            }
        }

        /// <summary>
        /// Removes the tag from the image.
        /// </summary>
        /// <param name="imageId">Identifier for the image</param>
        /// <returns>Returns True when the tag is succesfully removed.</returns>
        public async Task<bool> RemoveTag(long imageId)
        {
            var result = await Request(HttpMethod.Delete, $"/index/images/{imageId}/tag");

            return TestResult("IMAGE_TAG_REMOVED", result);
        }

        /// <summary>
        /// Loads the CBIR index from the given local server path.
        /// </summary>
        /// <param name="path">The path of the index file on the server</param>
        /// <returns>Returns True when the index is succesfully loaded</returns>
        public async Task<bool> LoadIndex(string path = "")
        {
            var json = JObject.FromObject(new
            {
                type = "LOAD",
                index_path = path
            });

            var result = await Request(HttpMethod.Post, "/index/io", json);

            return TestResult("INDEX_LOADED", result);
        }

        /// <summary>
        /// Saves the CBIR index to the given local server path.
        /// </summary>
        /// <param name="path">The path of the index file on the server</param>
        /// <returns>Returns True when the index is succesfully saved</returns>
        public async Task<bool> WriteIndex(string path = "")
        {
            var json = JObject.FromObject(new
            {
                type = "WRITE",
                index_path = path
            });

            var result = await Request(HttpMethod.Post, "/index/io", json);

            return TestResult("INDEX_WRITTEN", result);
        }

        /// <summary>
        /// Loads the tag index from the given local server path.
        /// </summary>
        /// <param name="path">The path of the index file on the server</param>
        /// <returns>Returns True when the index is succesfully loaded</returns>
        public async Task<bool> LoadIndexTags(string path = "")
        {
            var json = JObject.FromObject(new
            {
                type = "LOAD_TAGS",
                index_tags_path = path
            });

            var result = await Request(HttpMethod.Post, "/index/io", json);

            return TestResult("INDEX_TAGS_LOADED", result);
        }

        /// <summary>
        /// Saves the tag index to the given local server path.
        /// </summary>
        /// <param name="path">The path of the index file on the server</param>
        /// <returns>Returns True when the index is succesfully saved</returns>
        public async Task<bool> WriteIndexTags(string path = "")
        {
            var json = JObject.FromObject(new
            {
                type = "WRITE_TAGS",
                index_tags_path = path
            });

            var result = await Request(HttpMethod.Post, "/index/io", json);

            return TestResult("INDEX_TAGS_WRITTEN", result);
        }

        /// <summary>
        /// Clears the indexes that are currently loaded. Both the CBIR index 
        /// as the tags are cleared.
        /// </summary>
        /// <returns>Returns True when the indexes are succesfully cleared</returns>
        public async Task<bool> ClearIndex()
        {
            var json = JObject.FromObject(new
            {
                type = "CLEAR",
            });

            var result = await Request(HttpMethod.Post, "/index/io", json);

            return TestResult("INDEX_CLEARED", result);
        }

        /// <summary>
        /// Returnes all the image identifiers from the CBIR index.
        /// </summary>
        /// <returns>Array of identifiers</returns>
        public async Task<long[]> GetIndexImageIds()
        {
            var result = await Request(HttpMethod.Get, "/index/imageIds");

            if (TestResult("INDEX_IMAGE_IDS", result))
            {
                return result["image_ids"] is JArray arr
                    ? arr.Select(x => (long)x).ToArray<long>()
                    : new long[0];
            }

            return new long[0];
        }

        /// <summary>
        /// Execute a search query using the local file path.
        /// </summary>
        /// <param name="filePath">Local file</param>
        /// <returns>The images that matches the query image.</returns>
        public async Task<SearchResult[]> ImageQueryFile(string filePath)
        {
            return await ImageQueryData(await File.ReadAllBytesAsync(filePath));
        }

        /// <summary>
        /// Execute a search query using the image data.
        /// </summary>
        /// <param name="filePath">Local file</param>
        /// <returns>The images that matches the query image.</returns>
        public async Task<SearchResult[]> ImageQueryData(byte[] imageData)
        {
            using (var data = new ByteArrayContent(imageData))
            {
                data.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");

                var result = await Request(HttpMethod.Post, $"/index/searcher", data);

                if (TestResult("SEARCH_RESULTS", result))
                {
                    Console.WriteLine(result);

                    long[] imagesIds = result["image_ids"] is JArray _image_ids 
                        ? _image_ids.Select(x => (long)x).ToArray<long>() 
                        : new long[0];
                    string[] tags = result["tags"] is JArray _tags 
                        ? _tags.Select(x => (string)x).ToArray<string>() 
                        : new string[0];
                    double[] scores = result["scores"] is JArray _scores 
                        ? _scores.Select(x => (double)x).ToArray<double>() 
                        : new double[0];
                    Rectangle[] rects = result["bounding_rects"] is JArray _rects 
                        ? _rects.Select(x => new Rectangle(
                            (int)x["x"],
                            (int)x["y"],
                            (int)x["width"],
                            (int)x["height"]
                            )).ToArray<Rectangle>() 
                        : new Rectangle[0];

                    List<SearchResult> sResults = new List<SearchResult>();

                    for (int i = 0; i < imagesIds.Count(); i++)
                    {
                        sResults.Add(new SearchResult()
                        {
                            ImageId = imagesIds[i],
                            Tag = i < tags.Count() ? tags[i] : string.Empty,
                            Score = i < scores.Count() ? scores[i] : 0.0D,
                            BoundingRect = i < rects.Count() ? rects[i] : new Rectangle()
                        });
                    }

                    return sResults.ToArray();
                }
            }

            return null;
        }
    }
}
