<?PHP
## Updated by Timothy Rogers

// Pull in database infomation
include("databaseinfo.php");

$now = time();

// Connect to the database server, and display error if not working
$dbconnect = mysqli_connect($DB_HOST, $DB_USER, $DB_PASSWORD, $DB_NAME);
if (!$dbconnect) {
    die('Connect Error: ' . mysqli_connect_error());
}

#
#  Copyright (c) Fly Man (http://opensimulator.org/)
#

###################### No user serviceable parts below #####################

#
# The XMLRPC server object
#

$xmlrpc_server = xmlrpc_server_create();

#
# Send email to the database
#

xmlrpc_server_register_method($xmlrpc_server, "send_email",
		"send_email");

function send_email($method_name, $params, $app_data)
{
	$req 			= $params[0];

	$from			= $req['fromaddress'];
	$to				= $req['toaddress'];
	$timestamp		= $req['timestamp'];
	$region			= $req['region'];
	$object			= $req['objectname'];
	$objectlocation	= $req['position'];
	$subject	 	= $req['subject'];
	$message		= $req['messagebody'];

	//Escape Strings to avoid bad things from happening
	$from = mysqli_real_escape_string($dbconnect, $from);
	$to = mysqli_real_escape_string($dbconnect, $to);
	$timestamp = mysqli_real_escape_string($dbconnect, $timestamp);
	$region = mysqli_real_escape_string($dbconnect, $region);
	$object = mysqli_real_escape_string($dbconnect, $object);
	$objectlocation = mysqli_real_escape_string($dbconnect, $objectlocation);
	$subject = mysqli_real_escape_string($dbconnect, $subject);
	$message = mysqli_real_escape_string($dbconnect, $subject);

	//Insert the data into database
	$result = $dbconnect->query("INSERT INTO email VALUES('$to','
							$from','
							$timestamp','
							$region','
							$object','.
							$objectlocation','.
							$subject','
							$message')");

	$data = array();

	if ($mysqli->affected_rows > 0)
	{
		$data[] = array(
				"saved" => "Yes"
				);
	}
	else
	{
		$data[] = array(
				"saved" => "No"
				);
	}


	$response_xml = xmlrpc_encode(array(
		'success'	  => True,
		'errorMessage' => "",
		'data' => $data
	));

	print $response_xml;

	//Close the connection
	$result->close();
}

#
# Check if there's email in the database
#

xmlrpc_server_register_method($xmlrpc_server, "check_email",
		"check_email");

function check_email($method_name, $params, $app_data)
{
	$req 			= $params[0];

	$object			= $req['objectid'];

	$sql = "SELECT COUNT(*) as num FROM email WHERE `to` = '".mysql_escape_string($object)."'";

	$result = mysql_query($sql);

	$data = array();

	while (($row = mysql_fetch_assoc($result)))
	{
		$data[] = array(
			"num_emails" => $row['num']);
	}

	$response_xml = xmlrpc_encode(array(
		'success'	  => True,
		'errorMessage' => "",
		'data' => $data
	));

	print $response_xml;
}

#
# Retrieve messages from the database
#

xmlrpc_server_register_method($xmlrpc_server, "retrieve_email",
		"retrieve_email");

function retrieve_email($method_name, $params, $app_data)
{
	$req 			= $params[0];

	$object			= $req['objectid'];
	$rows			= $req['number'];

	$sql = "SELECT `timestamp`,`subject`, `from`,`objectname`,`region`,`objectlocation`,`message` FROM email WHERE `to` = '".mysql_escape_string($object)."' LIMIT 0,".$rows;

	$result = mysql_query($sql);

	$data = array();
	while ($row = mysql_fetch_assoc($result))
	{
		$data[] = array(
				"timestamp" => $row["timestamp"],
				"subject" => $row["subject"],
				"sender" => $row["from"],
				"objectname" => $row["objectname"],
				"region" => $row["region"],
				"objectpos" => $row["objectlocation"],
				"message" => $row["message"]);
	}

	// Now delete the email from the database

	$delete = "DELETE FROM email WHERE `to` = '".mysql_escape_string($object)."'";

	$result = mysql_query($delete);

	$response_xml = xmlrpc_encode(array(
		'success'	  => True,
		'errorMessage' => "",
		'data' => $data
	));

	print $response_xml;
}

#
# Process the request
#

$request_xml = $HTTP_RAW_POST_DATA;
xmlrpc_server_call_method($xmlrpc_server, $request_xml, '');
xmlrpc_server_destroy($xmlrpc_server);
?>
