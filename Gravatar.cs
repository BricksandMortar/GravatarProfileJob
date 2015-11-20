using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

using Quartz;
using RestSharp;
using Newtonsoft.Json.Linq;

using Rock;
using Rock.Attribute;
using Rock.Data;
using Rock.Model;
using Rock.Web.Cache;

namespace com.bricksandmortar.Gravatar
{

    /// <summary>
    /// Job to process communications
    /// </summary>
    [IntegerField( "Max Queries per Run", "The maximum number of people to query Gravatar for per run.", true, 2000 )]
    [IntegerField( "Photo Size", "The photo height in pixels to request from Gravatar.", true, 200 )]

    [DisallowConcurrentExecution]
    public class Gravatar : IJob
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FindGravatarImages"/> class.
        /// </summary>
        public Gravatar()
        {
        }

        /// <summary>
        /// Executes the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        public virtual void Execute( IJobExecutionContext context )
        {
            // Get the job map
            JobDataMap dataMap = context.JobDetail.JobDataMap;
            int maxQueries = Int32.Parse( dataMap.GetString( "MaxQueriesperRun" ) );
            int size = Int32.Parse( dataMap.GetString( "ImageSize" ) );

            // Find people with no photo
            var rockContext = new RockContext();
            PersonService personService = new PersonService( rockContext );
            var people = personService.Queryable()
                .Where( p => p.PhotoId == null )
                .Take( maxQueries )
                .ToList();
            foreach ( var person in people )
            {
                if ( !string.IsNullOrEmpty( person.Email ) )
                {
                    // Build MD5 hash for email
                    MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider();
                    byte[] encodedEmail = new UTF8Encoding().GetBytes( person.Email.ToLower().Trim() );
                    byte[] hashedBytes = md5.ComputeHash( encodedEmail );
                    StringBuilder sb = new StringBuilder( hashedBytes.Length * 2 );
                    for ( int i = 0; i < hashedBytes.Length; i++ )
                    {
                        sb.Append( hashedBytes[i].ToString( "X2" ) );
                    }
                    string hash = sb.ToString().ToLower();
                    // Query Gravatar's https endpoint asking for a 404 on no match
                    var restClient = new RestClient( string.Format( "https://secure.gravatar.com/avatar/{0}.jpg?default=404&size={1}", hash, size ) );
                    var request = new RestRequest( Method.GET );
                    var response = restClient.Execute( request );
                    if ( response.StatusCode == HttpStatusCode.OK )
                    {
                        var bytes = response.RawBytes;
                        // Create and save the image
                        BinaryFileType fileType = new BinaryFileTypeService( rockContext ).Get( Rock.SystemGuid.BinaryFiletype.PERSON_IMAGE.AsGuid() );
                        if ( fileType != null )
                        {

                            var binaryFileService = new BinaryFileService( rockContext );
                            var binaryFile = new BinaryFile();
                            binaryFileService.Add( binaryFile );
                            binaryFile.IsTemporary = false;
                            binaryFile.BinaryFileType = fileType;
                            binaryFile.MimeType = "image/jpeg";
                            binaryFile.FileName = person.NickName + person.LastName + ".jpg";
                            binaryFile.ContentStream = new MemoryStream( bytes );

                            person.PhotoId = binaryFile.Id;
                            rockContext.SaveChanges();
                        }
                    }
                    RestClient profileClient = new RestClient( "http://www.gravatar.com/" );
                    var profileRequest = new RestRequest( Method.GET );
                    profileRequest.RequestFormat = DataFormat.Json;
                    profileRequest.Resource = hash + ".json";
                    var profileResponse = profileClient.Execute( profileRequest );
                    if ( profileResponse.StatusCode == HttpStatusCode.OK )
                    {
                        JObject gravatarProfile = JObject.Parse( profileResponse.Content );

                        string gravatarFirstName = gravatarProfile["entry"][0]["name"]["givenName"].ToStringSafe();
                        if ( string.IsNullOrEmpty( person.FirstName ) && !string.IsNullOrEmpty( gravatarFirstName ) )
                        {
                            person.FirstName = gravatarFirstName;
                        }
                        string gravatarLastName = gravatarProfile["entry"][0]["name"]["familyName"].ToStringSafe();
                        if ( string.IsNullOrEmpty( person.LastName ) && !string.IsNullOrEmpty( gravatarLastName ) )
                        {
                            person.LastName = gravatarLastName;
                        }

                        var twitterAttribute = AttributeCache.Read( Rock.SystemGuid.Attribute.PERSON_TWITTER.AsGuid() );
                        if ( twitterAttribute != null )
                        {
                            person.LoadAttributes( rockContext );
                            if ( string.IsNullOrEmpty( person.GetAttributeValue( twitterAttribute.Key ) ) )
                            {
                                var gravatarTwitterAccount = gravatarProfile["entry"][0]["accounts"].Children()
                            .Where( a => a["shortname"].ToStringSafe() == "twitter" )
                            .FirstOrDefault();
                                if ( gravatarTwitterAccount["verified"].ToString().AsBoolean() )
                                {
                                    string twitterLink = gravatarTwitterAccount["url"].ToStringSafe();
                                    if ( twitterLink != null )
                                    {
                                        person.SetAttributeValue( twitterAttribute.Key, twitterLink );
                                    }
                                }
                            }
                        }

                        var facebookAttribute = AttributeCache.Read( Rock.SystemGuid.Attribute.PERSON_FACEBOOK.AsGuid() );
                        if ( facebookAttribute != null )
                        {
                            person.LoadAttributes( rockContext );
                            if ( string.IsNullOrEmpty( person.GetAttributeValue( facebookAttribute.Key ) ) )
                            {
                                var gravatarFacebookAccount = gravatarProfile["entry"][0]["accounts"].Children()
                            .Where( a => a["shortname"].ToStringSafe() == "facebook" )
                            .FirstOrDefault();
                                if ( gravatarFacebookAccount["verified"].ToString().AsBoolean() )
                                {
                                    string facebookLink = gravatarFacebookAccount["url"].ToStringSafe();
                                    if ( facebookLink != null )
                                    {
                                        person.SetAttributeValue( facebookAttribute.Key, facebookLink );
                                    }
                                }
                            }
                        }

                        person.SaveAttributeValues( rockContext );
                    }
                }
            }
        }
    }
}
