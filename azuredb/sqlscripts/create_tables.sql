CREATE TABLE Activities (
	uuid varchar(40) PRIMARY KEY,
	converter varchar(20),
	progress varchar(20),
	avg_speed_in_kmh decimal,
	avg_heart_rate integer,
	total_dist_in_km decimal,
	total_time_in_sec decimal
)

CREATE TABLE Records (
	activity_uuid varchar(40),
	[timestamp] varchar(20),
	lat decimal,
	lon decimal,
	distance decimal,
	ele decimal,
	speed decimal,
	heart_rate integer
)